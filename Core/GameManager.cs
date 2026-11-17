using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    public class McVersion
    {
        public string Id;
        public string Type;   // release / snapshot / old_beta ... / modpack
        public string Url;
        public string Sha1;
        public string PackId;     // si c'est un modpack installé
        public string PackName;
        public override string ToString() =>
            Type == "modpack" ? $"📦 {PackName}  ({Id})" : Type == "release" ? Id : $"{Id}  ({Type})";
    }

    /// <summary>
    /// Télécharge et lance Minecraft (vanilla + Fabric).
    /// </summary>
    public class GameManager
    {
        private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        private const string JavaIndexUrl =
            "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        private const string FabricMeta = "https://meta.fabricmc.net/v2";

        private readonly HttpClient _http;
        private readonly Downloader _dl;

        public Action<string> Status;
        public Action<double> Progress;   // 0..1, ou -1 pour indéterminé

        public GameManager(HttpClient http)
        {
            _http = http;
            _dl = new Downloader(http);
        }

        // ----------------------------------------------------------------- versions

        public async Task<List<McVersion>> GetVersionsAsync()
        {
            var json = JsonNode.Parse(await _http.GetStringAsync(ManifestUrl));
            return json["versions"].AsArray().Select(v => new McVersion
            {
                Id = (string)v["id"],
                Type = (string)v["type"],
                Url = (string)v["url"],
                Sha1 = (string)v["sha1"]
            }).ToList();
        }

        // ----------------------------------------------------------------- installation

        /// <summary>Installe tout ce qu'il faut et renvoie l'id de version à lancer.</summary>
        public async Task<string> InstallAsync(McVersion version, bool fabric, CancellationToken ct, string loaderVersion = null)
        {
            Status?.Invoke("Récupération de la version " + version.Id + "...");
            Progress?.Invoke(-1);
            var vDir = Path.Combine(Paths.Versions, version.Id);
            var vJsonPath = Path.Combine(vDir, version.Id + ".json");
            await _dl.RunAsync(new[] { new DownloadJob { Url = version.Url, Path = vJsonPath, Sha1 = version.Sha1 } }, null, ct);

            var launchId = version.Id;
            if (fabric)
                launchId = await InstallFabricAsync(version.Id, ct, loaderVersion);

            var vjson = LoadMerged(launchId);
            var baseId = BaseId(launchId);

            // Jar client
            var client = vjson["downloads"]?["client"];
            var jobs = new List<DownloadJob>();
            if (client != null)
                jobs.Add(new DownloadJob
                {
                    Url = (string)client["url"],
                    Sha1 = (string)client["sha1"],
                    Size = (long?)client["size"] ?? 0,
                    Path = Path.Combine(Paths.Versions, baseId, baseId + ".jar")
                });

            // Librairies
            foreach (var lib in vjson["libraries"].AsArray())
            {
                if (!RulesAllow(lib["rules"] as JsonArray)) continue;
                var art = lib["downloads"]?["artifact"];
                if (art != null && art["url"] != null && (string)art["url"] != "")
                {
                    jobs.Add(new DownloadJob
                    {
                        Url = (string)art["url"],
                        Sha1 = (string)art["sha1"],
                        Size = (long?)art["size"] ?? 0,
                        Path = Path.Combine(Paths.Libraries, (string)art["path"])
                    });
                }
                else if (lib["downloads"] == null && lib["name"] != null)
                {
                    // Format Maven (Fabric) : "groupe:nom:version" + url de base
                    var rel = MavenPath((string)lib["name"]);
                    var baseUrl = ((string)lib["url"] ?? "https://libraries.minecraft.net/").TrimEnd('/') + "/";
                    jobs.Add(new DownloadJob
                    {
                        Url = baseUrl + rel.Replace('\\', '/'),
                        Sha1 = (string)lib["sha1"],
                        Size = (long?)lib["size"] ?? 0,
                        Path = Path.Combine(Paths.Libraries, rel)
                    });
                }

                // Natives à l'ancienne (versions < 1.19)
                var nativeKey = (string)lib["natives"]?["windows"];
                if (nativeKey != null)
                {
                    nativeKey = nativeKey.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    var cls = lib["downloads"]?["classifiers"]?[nativeKey];
                    if (cls != null)
                        jobs.Add(new DownloadJob
                        {
                            Url = (string)cls["url"],
                            Sha1 = (string)cls["sha1"],
                            Size = (long?)cls["size"] ?? 0,
                            Path = Path.Combine(Paths.Libraries, (string)cls["path"])
                        });
                }
            }

            Status?.Invoke("Téléchargement des fichiers du jeu...");
            await _dl.RunAsync(jobs, ProgressReporter(), ct);

            await InstallAssetsAsync(vjson, ct);
            ExtractNatives(vjson, launchId);
            return launchId;
        }

        private async Task InstallAssetsAsync(JsonNode vjson, CancellationToken ct)
        {
            var ai = vjson["assetIndex"];
            if (ai == null) return;
            var id = (string)ai["id"];
            var indexPath = Path.Combine(Paths.Assets, "indexes", id + ".json");
            await _dl.RunAsync(new[] { new DownloadJob { Url = (string)ai["url"], Path = indexPath, Sha1 = (string)ai["sha1"] } }, null, ct);

            var index = JsonNode.Parse(File.ReadAllText(indexPath));
            var objects = index["objects"].AsObject();
            var jobs = new List<DownloadJob>();
            foreach (var kv in objects)
            {
                var hash = (string)kv.Value["hash"];
                var sub = hash.Substring(0, 2);
                jobs.Add(new DownloadJob
                {
                    Url = $"https://resources.download.minecraft.net/{sub}/{hash}",
                    Path = Path.Combine(Paths.Assets, "objects", sub, hash),
                    Sha1 = hash,
                    Size = (long?)kv.Value["size"] ?? 0
                });
            }
            Status?.Invoke("Téléchargement des sons et textures...");
            await _dl.RunAsync(jobs, ProgressReporter(), ct, 32);

            // Très vieilles versions : assets "virtuels"
            bool isVirtual = (bool?)index["virtual"] == true || (bool?)index["map_to_resources"] == true;
            if (isVirtual)
            {
                var target = Path.Combine(Paths.Assets, "virtual", id);
                foreach (var kv in objects)
                {
                    var hash = (string)kv.Value["hash"];
                    var dst = Path.Combine(target, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(dst)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(Path.Combine(Paths.Assets, "objects", hash.Substring(0, 2), hash), dst);
                }
            }
        }

        private void ExtractNatives(JsonNode vjson, string launchId)
        {
            var nativesDir = NativesDir(launchId);
            Directory.CreateDirectory(nativesDir);
            foreach (var lib in vjson["libraries"].AsArray())
            {
                if (!RulesAllow(lib["rules"] as JsonArray)) continue;
                var nativeKey = (string)lib["natives"]?["windows"];
                if (nativeKey == null) continue;
                nativeKey = nativeKey.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                var cls = lib["downloads"]?["classifiers"]?[nativeKey];
                if (cls == null) continue;
                var jar = Path.Combine(Paths.Libraries, (string)cls["path"]);
                if (!File.Exists(jar)) continue;
                using var zip = ZipFile.OpenRead(jar);
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/")) continue;
                    var dst = Path.Combine(nativesDir, e.Name);
                    if (!File.Exists(dst)) e.ExtractToFile(dst, false);
                }
            }
        }

        // ----------------------------------------------------------------- Fabric

        private async Task<string> InstallFabricAsync(string mcVersion, CancellationToken ct, string wantedLoader = null)
        {
            Status?.Invoke("Installation de Fabric...");
            var loaders = JsonNode.Parse(await _http.GetStringAsync($"{FabricMeta}/versions/loader/{mcVersion}", ct)).AsArray();
            if (loaders.Count == 0)
                throw new Exception($"Fabric n'existe pas pour la version {mcVersion}.");

            var pick = (wantedLoader == null ? null : loaders.FirstOrDefault(l => (string)l["loader"]?["version"] == wantedLoader))
                       ?? loaders.FirstOrDefault(l => (bool?)l["loader"]?["stable"] == true) ?? loaders[0];
            var loaderVer = (string)pick["loader"]["version"];

            var profileText = await _http.GetStringAsync($"{FabricMeta}/versions/loader/{mcVersion}/{loaderVer}/profile/json", ct);
            var profile = JsonNode.Parse(profileText);
            var id = (string)profile["id"];
            var dir = Path.Combine(Paths.Versions, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, id + ".json"), profileText);
            return id;
        }

        // ----------------------------------------------------------------- Java

        public async Task<string> EnsureJavaAsync(string launchId, CancellationToken ct)
        {
            var vjson = LoadMerged(launchId);
            var component = (string)vjson["javaVersion"]?["component"] ?? "jre-legacy";
            var javaw = Path.Combine(Paths.Runtime, component, "bin", "javaw.exe");
            if (File.Exists(javaw)) return javaw;

            Status?.Invoke("Téléchargement de Java (" + component + ")...");
            Progress?.Invoke(-1);
            var all = JsonNode.Parse(await _http.GetStringAsync(JavaIndexUrl, ct));
            var platform = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "windows-arm64"
                : Environment.Is64BitOperatingSystem ? "windows-x64" : "windows-x86";
            var entry = all[platform]?[component]?.AsArray().FirstOrDefault();
            if (entry == null) throw new Exception("Java introuvable pour " + component + ". Mets un chemin Java dans les paramètres.");

            var manifest = JsonNode.Parse(await _http.GetStringAsync((string)entry["manifest"]["url"], ct));
            var root = Path.Combine(Paths.Runtime, component);
            var jobs = new List<DownloadJob>();
            foreach (var kv in manifest["files"].AsObject())
            {
                var type = (string)kv.Value["type"];
                var target = Path.Combine(root, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (type == "directory") { Directory.CreateDirectory(target); continue; }
                if (type != "file") continue;
                var raw = kv.Value["downloads"]["raw"];
                jobs.Add(new DownloadJob
                {
                    Url = (string)raw["url"],
                    Sha1 = (string)raw["sha1"],
                    Size = (long?)raw["size"] ?? 0,
                    Path = target
                });
            }
            await _dl.RunAsync(jobs, ProgressReporter(), ct);
            if (!File.Exists(javaw)) throw new Exception("Installation de Java échouée.");
            return javaw;
        }

        // ----------------------------------------------------------------- lancement

        public Process Launch(string launchId, McAccount acc, Settings s, string javaPath, string gameDir,
            string joinServer = null)
        {
            var vjson = LoadMerged(launchId);
            var baseId = BaseId(launchId);
            Directory.CreateDirectory(gameDir);
            var nativesDir = NativesDir(launchId);

            // Classpath (sans doublons : la version enfant (Fabric) passe en premier)
            var cp = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lib in vjson["libraries"].AsArray())
            {
                if (!RulesAllow(lib["rules"] as JsonArray)) continue;
                if (lib["natives"] != null && lib["downloads"]?["artifact"] == null) continue;
                var name = (string)lib["name"] ?? "";
                var parts = name.Split(':');
                var key = parts.Length >= 2 ? parts[0] + ":" + parts[1] + (parts.Length > 3 ? ":" + parts[3] : "") : name;
                if (!seen.Add(key)) continue;

                string path = null;
                var art = lib["downloads"]?["artifact"];
                if (art?["path"] != null) path = Path.Combine(Paths.Libraries, (string)art["path"]);
                else if (lib["downloads"] == null && name != "") path = Path.Combine(Paths.Libraries, MavenPath(name));
                if (path != null && File.Exists(path)) cp.Add(path);
            }
            cp.Add(Path.Combine(Paths.Versions, baseId, baseId + ".jar"));
            var classpath = string.Join(";", cp);

            var assetIndex = (string)vjson["assetIndex"]?["id"] ?? "legacy";
            var vars = new Dictionary<string, string>
            {
                ["auth_player_name"] = acc.Name,
                ["version_name"] = launchId,
                ["game_directory"] = gameDir,
                ["assets_root"] = Paths.Assets,
                ["game_assets"] = Path.Combine(Paths.Assets, "virtual", assetIndex),
                ["assets_index_name"] = assetIndex,
                ["auth_uuid"] = acc.Uuid,
                ["auth_access_token"] = acc.AccessToken,
                ["auth_session"] = acc.AccessToken,
                ["auth_xuid"] = acc.Xuid ?? "0",
                ["clientid"] = string.IsNullOrWhiteSpace(s.ClientId) ? MicrosoftAuth.OfficialClientId : s.ClientId,
                ["user_type"] = "msa",
                ["user_properties"] = "{}",
                ["version_type"] = "Velox Client",
                ["natives_directory"] = nativesDir,
                ["launcher_name"] = "velox-client",
                ["launcher_version"] = Updater.CurrentVersionText,
                ["classpath"] = classpath,
                ["classpath_separator"] = ";",
                ["library_directory"] = Paths.Libraries
            };

            var jvm = new List<string>();
            var game = new List<string>();

            if (vjson["arguments"] != null)
            {
                CollectArgs(vjson["arguments"]["jvm"] as JsonArray, jvm);
                CollectArgs(vjson["arguments"]["game"] as JsonArray, game);
            }
            if (vjson["minecraftArguments"] != null)
                game.AddRange(((string)vjson["minecraftArguments"]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (jvm.Count == 0)
            {
                jvm.Add("-Djava.library.path=${natives_directory}");
                jvm.Add("-cp");
                jvm.Add("${classpath}");
            }

            var args = s.LauncherBoost ? Boost.JvmArgs(s.RamMb) : new List<string>
            {
                $"-Xmx{s.RamMb}M",
                $"-Xms{Math.Min(1024, s.RamMb)}M",
                "-XX:+UseG1GC",
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:G1NewSizePercent=20",
                "-XX:MaxGCPauseMillis=50"
            };
            if (s.LauncherBoost) Boost.PreferHighPerformanceGpu(javaPath);
            if (!string.IsNullOrWhiteSpace(s.ExtraJvmArgs))
                args.AddRange(s.ExtraJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            args.AddRange(jvm.Select(a => Fill(a, vars)));
            args.Add((string)vjson["mainClass"]);
            args.AddRange(game.Select(a => Fill(a, vars)));
            // Rejoindre un serveur directement (Minecraft 1.20+)
            if (!string.IsNullOrWhiteSpace(joinServer))
            {
                args.Add("--quickPlayMultiplayer");
                args.Add(joinServer.Trim());
            }

            var psi = new ProcessStartInfo(javaPath)
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            Log.Write("Lancement de " + launchId + " avec " + javaPath);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Write("[jeu] " + e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Write("[jeu] " + e.Data); };
            p.Start();
            if (s.LauncherBoost) Boost.ApplyToProcess(p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        // ----------------------------------------------------------------- utilitaires

        private static string NativesDir(string id) => Path.Combine(Paths.Versions, id, "natives");

        /// <summary>Fusionne une version avec sa version parente (inheritsFrom), comme Fabric.</summary>
        private static JsonNode LoadMerged(string id)
        {
            var path = Path.Combine(Paths.Versions, id, id + ".json");
            var child = JsonNode.Parse(File.ReadAllText(path));
            var parentId = (string)child["inheritsFrom"];
            if (parentId == null) return child;

            var parent = LoadMerged(parentId);
            var merged = parent.DeepClone().AsObject();

            foreach (var kv in child.AsObject())
            {
                if (kv.Key == "libraries")
                {
                    var libs = new JsonArray();
                    foreach (var l in kv.Value.AsArray()) libs.Add(l.DeepClone());
                    foreach (var l in parent["libraries"].AsArray()) libs.Add(l.DeepClone());
                    merged["libraries"] = libs;
                }
                else if (kv.Key == "arguments")
                {
                    var oldArgs = merged["arguments"] as JsonObject;
                    var args = new JsonObject();
                    foreach (var side in new[] { "game", "jvm" })
                    {
                        var arr = new JsonArray();
                        if (oldArgs?[side] is JsonArray pa) foreach (var a in pa) arr.Add(a?.DeepClone());
                        if (kv.Value?[side] is JsonArray ca) foreach (var a in ca) arr.Add(a?.DeepClone());
                        args[side] = arr;
                    }
                    merged["arguments"] = args;
                }
                else if (kv.Key != "inheritsFrom")
                {
                    merged[kv.Key] = kv.Value?.DeepClone();
                }
            }
            return merged;
        }

        /// <summary>Renvoie l'id de la version vanilla de base (pour le .jar).</summary>
        private static string BaseId(string id)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Paths.Versions, id, id + ".json")));
            var parent = (string)json["inheritsFrom"];
            return parent == null ? id : BaseId(parent);
        }

        private static string MavenPath(string name)
        {
            // groupe:artefact:version[:classifier]
            var p = name.Split(':');
            var file = p[1] + "-" + p[2] + (p.Length > 3 ? "-" + p[3] : "") + ".jar";
            return Path.Combine(p[0].Replace('.', Path.DirectorySeparatorChar), p[1], p[2], file);
        }

        private static void CollectArgs(JsonArray arr, List<string> into)
        {
            if (arr == null) return;
            foreach (var a in arr)
            {
                if (a is JsonValue) { into.Add((string)a); continue; }
                if (!RulesAllow(a["rules"] as JsonArray)) continue;
                var v = a["value"];
                if (v is JsonArray va) into.AddRange(va.Select(x => (string)x));
                else if (v != null) into.Add((string)v);
            }
        }

        private static bool RulesAllow(JsonArray rules)
        {
            if (rules == null || rules.Count == 0) return true;
            bool allowed = false;
            foreach (var r in rules)
            {
                bool match = true;
                var os = r["os"];
                if (os != null)
                {
                    var name = (string)os["name"];
                    if (name != null && name != "windows") match = false;
                    var arch = (string)os["arch"];
                    if (arch == "x86" && Environment.Is64BitOperatingSystem) match = false;
                }
                // Les "features" (démo, résolution perso, quick play...) sont désactivées
                if (r["features"] is JsonObject) match = false;
                if (match) allowed = (string)r["action"] == "allow";
            }
            return allowed;
        }

        private static string Fill(string s, Dictionary<string, string> vars)
        {
            foreach (var kv in vars) s = s.Replace("${" + kv.Key + "}", kv.Value);
            return s;
        }

        private IProgress<(int done, int total)> ProgressReporter() =>
            new Progress<(int done, int total)>(p =>
            {
                Progress?.Invoke(p.total == 0 ? 1 : (double)p.done / p.total);
            });
    }
}
