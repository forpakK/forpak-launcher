using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    public class ModHit
    {
        public string ProjectId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Info { get; set; }
        public string IconUrl { get; set; }
    }

    public class InstalledMod
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
    }

    /// <summary>Mods Fabric : Modrinth + Forpak Client. Un dossier séparé par version.</summary>
    public class ModManager
    {
        private const string Api = "https://api.modrinth.com/v2";
        public const string FabricApiId = "P7dR8mSH";
        public static readonly string[] ForpakClientVersions = { "1.21.1", "1.21.11" };

        private readonly HttpClient _http;
        private readonly Downloader _dl;

        public ModManager(HttpClient http)
        {
            _http = http;
            _dl = new Downloader(http);
        }

        // ----------------------------------------------------------- dossiers

        /// <summary>Dossier de jeu : vanilla = dossier principal, Fabric = une instance par version.</summary>
        public static string GameDir(string mcVersion, bool fabric) =>
            fabric ? Path.Combine(Paths.Root, "instances", mcVersion + "-fabric") : Paths.Root;

        public static string ModsDir(string mcVersion) => Path.Combine(GameDir(mcVersion, true), "mods");

        private static string IndexFile(string mc) => Path.Combine(ModsDir(mc), ".forpak-mods.json");

        private static Dictionary<string, string> LoadIndex(string mc)
        {
            try
            {
                if (File.Exists(IndexFile(mc)))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IndexFile(mc)))
                           ?? new Dictionary<string, string>();
            }
            catch { }
            return new Dictionary<string, string>();
        }

        private static void SaveIndex(string mc, Dictionary<string, string> idx)
        {
            Directory.CreateDirectory(ModsDir(mc));
            File.WriteAllText(IndexFile(mc), JsonSerializer.Serialize(idx));
        }

        // ----------------------------------------------------------- Modrinth

        public async Task<List<ModHit>> SearchAsync(string query, string mc)
        {
            var facets = $"[[\"project_type:mod\"],[\"categories:fabric\"],[\"versions:{mc}\"]]";
            var url = $"{Api}/search?limit=25&index=relevance&query={Uri.EscapeDataString(query ?? "")}" +
                      $"&facets={Uri.EscapeDataString(facets)}";
            var json = JsonNode.Parse(await _http.GetStringAsync(url));
            return json["hits"].AsArray().Select(h => new ModHit
            {
                ProjectId = (string)h["project_id"],
                Title = (string)h["title"],
                Description = (string)h["description"],
                IconUrl = string.IsNullOrEmpty((string)h["icon_url"]) ? null : (string)h["icon_url"],
                Info = $"{(string)h["author"]} · {FormatDownloads((long?)h["downloads"] ?? 0)} téléchargements"
            }).ToList();
        }

        /// <summary>Installe la dernière version d'un mod (+ ses dépendances obligatoires).</summary>
        public async Task<string> InstallAsync(string projectId, string mc, bool update = false, HashSet<string> visiting = null)
        {
            visiting ??= new HashSet<string>();
            if (!visiting.Add(projectId)) return null;

            var idx = LoadIndex(mc);
            if (!update && idx.TryGetValue(projectId, out var existing) && File.Exists(Path.Combine(ModsDir(mc), existing)))
                return existing;

            var url = $"{Api}/project/{projectId}/version?loaders={Uri.EscapeDataString("[\"fabric\"]")}" +
                      $"&game_versions={Uri.EscapeDataString($"[\"{mc}\"]")}";
            var versions = JsonNode.Parse(await _http.GetStringAsync(url)).AsArray();
            if (versions.Count == 0)
                throw new Exception($"Ce mod n'existe pas pour Fabric {mc}.");

            var ver = versions.FirstOrDefault(v => (string)v["version_type"] == "release") ?? versions[0];
            var files = ver["files"].AsArray();
            var file = files.FirstOrDefault(f => (bool?)f["primary"] == true) ?? files[0];
            var fileName = (string)file["filename"];

            // Supprime l'ancienne version du mod si elle existe
            if (idx.TryGetValue(projectId, out var old) && old != fileName)
            {
                try { File.Delete(Path.Combine(ModsDir(mc), old)); } catch { }
            }

            await _dl.DownloadOneAsync(new DownloadJob
            {
                Url = (string)file["url"],
                Path = Path.Combine(ModsDir(mc), fileName),
                Sha1 = (string)file["hashes"]?["sha1"]
            }, CancellationToken.None);

            idx = LoadIndex(mc);
            idx[projectId] = fileName;
            SaveIndex(mc, idx);

            // Dépendances obligatoires
            if (ver["dependencies"] is JsonArray deps)
            {
                foreach (var d in deps)
                {
                    var depId = (string)d["project_id"];
                    if ((string)d["dependency_type"] == "required" && !string.IsNullOrEmpty(depId))
                        await InstallAsync(depId, mc, false, visiting);
                }
            }
            return fileName;
        }

        public List<InstalledMod> GetInstalled(string mc)
        {
            var dir = ModsDir(mc);
            if (!Directory.Exists(dir)) return new List<InstalledMod>();
            return Directory.GetFiles(dir, "*.jar")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .Select(f => new InstalledMod { FileName = Path.GetFileName(f), FullPath = f })
                .ToList();
        }

        public void Remove(string mc, InstalledMod mod)
        {
            File.Delete(mod.FullPath);
            var idx = LoadIndex(mc);
            foreach (var key in idx.Where(kv => kv.Value == mod.FileName).Select(kv => kv.Key).ToList())
                idx.Remove(key);
            SaveIndex(mc, idx);
        }

        // ----------------------------------------------------------- Forpak Client

        public static bool SupportsForpakClient(string mc) => ForpakClientVersions.Contains(mc);

        // ----------------------------------------------------------- Pack FPS

        /// <summary>Mods d'optimisation (slugs Modrinth). Ceux qui n'existent pas pour la version sont sautés.</summary>
        public static readonly (string Slug, string Name)[] PerformanceMods =
        {
            ("sodium", "Sodium"),
            ("lithium", "Lithium"),
            ("ferrite-core", "FerriteCore"),
            ("immediatelyfast", "ImmediatelyFast"),
            ("entityculling", "EntityCulling"),
            ("modernfix", "ModernFix"),
            ("dynamic-fps", "Dynamic FPS"),
        };

        public async Task InstallPerformancePackAsync(string mc, Action<string> status)
        {
            foreach (var (slug, name) in PerformanceMods)
            {
                status?.Invoke("Pack FPS : " + name + "...");
                try { await InstallAsync(slug, mc); }
                catch (Exception ex) { Log.Write($"Pack FPS : {name} ignoré ({ex.Message})"); }
            }
        }

        /// <summary>Retire les mods du pack FPS (quand on le désactive).</summary>
        public void RemovePerformancePack(string mc)
        {
            var idx = LoadIndex(mc);
            bool changed = false;
            foreach (var (slug, _) in PerformanceMods)
            {
                if (!idx.TryGetValue(slug, out var file)) continue;
                try { File.Delete(Path.Combine(ModsDir(mc), file)); } catch { }
                idx.Remove(slug);
                changed = true;
            }
            if (changed) SaveIndex(mc, idx);
        }

        /// <summary>Installe / met à jour Velox Client (mod) + Fabric API depuis GitHub.</summary>
        public async Task EnsureForpakClientAsync(string mc, string remoteVersion, Action<string> status)
        {
            if (!SupportsForpakClient(mc)) return;
            var dir = ModsDir(mc);
            Directory.CreateDirectory(dir);

            status?.Invoke("Vérification de Fabric API...");
            await InstallAsync(FabricApiId, mc);

            RemoveLegacy(mc);
            var jar = Path.Combine(dir, $"veloxclient-{mc}.jar");
            var verFile = Path.Combine(dir, ".veloxclient-version");
            var local = File.Exists(verFile) ? File.ReadAllText(verFile).Trim() : "";

            // remoteVersion == null : pas d'internet / pas de release -> on garde ce qu'on a
            if (File.Exists(jar) && (remoteVersion == null || local == remoteVersion)) return;
            if (remoteVersion == null) return;

            status?.Invoke("Mise à jour de Velox Client...");
            await _dl.DownloadOneAsync(new DownloadJob
            {
                Url = Updater.ReleaseFileUrl($"veloxclient-{mc}.jar"),
                Path = jar
            }, CancellationToken.None);
            File.WriteAllText(verFile, remoteVersion);
        }

        public static void RemoveForpakClient(string mc)
        {
            RemoveLegacy(mc);
            try { File.Delete(Path.Combine(ModsDir(mc), $"veloxclient-{mc}.jar")); } catch { }
            try { File.Delete(Path.Combine(ModsDir(mc), ".veloxclient-version")); } catch { }
        }

        /// <summary>Supprime l'ancien mod (nom Forpak Client).</summary>
        private static void RemoveLegacy(string mc)
        {
            try { File.Delete(Path.Combine(ModsDir(mc), $"forpakclient-{mc}.jar")); } catch { }
            try { File.Delete(Path.Combine(ModsDir(mc), ".forpakclient-version")); } catch { }
        }

        private static string FormatDownloads(long n) =>
            n >= 1_000_000 ? $"{n / 1_000_000.0:0.#} M" : n >= 1000 ? $"{n / 1000.0:0.#} k" : n.ToString();
    }
}
