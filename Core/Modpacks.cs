using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    public class ModpackInstance
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string McVersion { get; set; }
        public string LoaderVersion { get; set; }
        public string IconUrl { get; set; }
        public string Version { get; set; }
        public int ModCount { get; set; }

        /// <summary>Fichiers qui n'ont pas pu être téléchargés (pas sauvegardé).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<string> FailedFiles { get; set; } = new();

        public string Dir => Path.Combine(Paths.Root, "instances", "pack-" + Id);
        public string Info => $"Minecraft {McVersion} · Fabric · {ModCount} fichiers";
    }

    /// <summary>Modpacks Modrinth (.mrpack) : recherche, installation, import d'un fichier.</summary>
    public class Modpacks
    {
        private const string Api = "https://api.modrinth.com/v2";
        private readonly HttpClient _http;
        private readonly Downloader _dl;

        public Modpacks(HttpClient http)
        {
            _http = http;
            _dl = new Downloader(http);
        }

        public async Task<(List<ModHit> hits, int total)> SearchAsync(string query, int offset)
        {
            var facets = "[[\"project_type:modpack\"],[\"categories:fabric\"]]";
            var url = $"{Api}/search?limit={ModManager.PageSize}&offset={offset}&index=" +
                      (string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance") +
                      $"&query={Uri.EscapeDataString(query ?? "")}&facets={Uri.EscapeDataString(facets)}";
            var json = JsonNode.Parse(await _http.GetStringAsync(url));
            var hits = json["hits"].AsArray().Select(h =>
            {
                var versions = (h["versions"] as JsonArray ?? new JsonArray()).Select(v => (string)v).ToList();
                var latest = versions.LastOrDefault(v => !v.Contains('w') && !v.Contains('-')) ?? versions.LastOrDefault();
                return new ModHit
                {
                    ProjectId = (string)h["project_id"],
                    Slug = (string)h["slug"],
                    Title = (string)h["title"],
                    Author = (string)h["author"],
                    Info = $"par {(string)h["author"]} · MC {latest}",
                    Description = (string)h["description"],
                    IconUrl = string.IsNullOrEmpty((string)h["icon_url"]) ? null : (string)h["icon_url"],
                    Downloads = ModManager.FormatDownloads((long?)h["downloads"] ?? 0),
                    Follows = ModManager.FormatDownloads((long?)h["follows"] ?? 0),
                    InstallLabel = "Installer le pack"
                };
            }).ToList();
            return (hits, (int?)json["total_hits"] ?? hits.Count);
        }

        /// <summary>Télécharge la dernière version Fabric d'un modpack Modrinth puis l'installe.</summary>
        public async Task<ModpackInstance> InstallFromModrinthAsync(ModHit hit, Action<string> status, IProgress<(int, int)> progress)
        {
            status?.Invoke("Recherche de la dernière version de " + hit.Title + "...");
            var url = $"{Api}/project/{hit.ProjectId}/version?loaders={Uri.EscapeDataString("[\"fabric\"]")}";
            var versions = JsonNode.Parse(await _http.GetStringAsync(url)).AsArray();
            var ver = versions.FirstOrDefault(v => (string)v["version_type"] == "release") ?? versions.FirstOrDefault();
            if (ver == null) throw new Exception("Ce modpack n'a pas de version Fabric.");
            var file = ver["files"].AsArray().FirstOrDefault(f => ((string)f["filename"] ?? "").EndsWith(".mrpack"))
                       ?? ver["files"].AsArray().FirstOrDefault();
            if (file == null) throw new Exception("Fichier du modpack introuvable.");

            var tmp = Path.Combine(Path.GetTempPath(), "velox-" + Guid.NewGuid().ToString("N") + ".mrpack");
            status?.Invoke("Téléchargement du modpack...");
            await _dl.DownloadOneAsync(new DownloadJob { Url = (string)file["url"], Path = tmp }, CancellationToken.None);
            try { return await ImportAsync(tmp, hit.Slug ?? hit.ProjectId, hit.IconUrl, status, progress); }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>Installe un fichier .mrpack (Modrinth) dans sa propre instance.</summary>
        public async Task<ModpackInstance> ImportAsync(string mrpackPath, string idHint, string iconUrl,
            Action<string> status, IProgress<(int, int)> progress)
        {
            // Ouverture en lecture partagée : marche même si le fichier est encore ouvert ailleurs
            using var fileStream = new FileStream(mrpackPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
            var indexEntry = zip.GetEntry("modrinth.index.json")
                             ?? throw new Exception("Ce fichier n'est pas un modpack Modrinth (.mrpack).");
            JsonNode index;
            using (var sr = new StreamReader(indexEntry.Open())) index = JsonNode.Parse(await sr.ReadToEndAsync());

            var deps = index["dependencies"];
            var mc = (string)deps?["minecraft"] ?? throw new Exception("Version de Minecraft absente du modpack.");
            var loader = (string)deps?["fabric-loader"];
            if (loader == null)
                throw new Exception("Ce modpack n'utilise pas Fabric (Forge/Quilt/NeoForge ne sont pas encore pris en charge).");

            var name = (string)index["name"] ?? Path.GetFileNameWithoutExtension(mrpackPath);
            var pack = new ModpackInstance
            {
                Id = SafeId(idHint ?? name),
                Name = name,
                McVersion = mc,
                LoaderVersion = loader,
                IconUrl = iconUrl,
                Version = (string)index["versionId"]
            };
            var root = Path.GetFullPath(pack.Dir);
            Directory.CreateDirectory(root);

            // Fichiers à télécharger
            var jobs = new List<DownloadJob>();
            foreach (var f in index["files"]?.AsArray() ?? new JsonArray())
            {
                if ((string)f["env"]?["client"] == "unsupported") continue;
                var rel = (string)f["path"];
                var target = SafeCombine(root, rel);
                var urlNode = f["downloads"]?.AsArray().FirstOrDefault();
                if (target == null || urlNode == null) continue;
                jobs.Add(new DownloadJob
                {
                    Url = (string)urlNode,
                    Path = target,
                    Sha1 = (string)f["hashes"]?["sha1"],
                    Size = (long?)f["fileSize"] ?? 0
                });
            }
            pack.ModCount = jobs.Count;
            status?.Invoke($"Téléchargement des {jobs.Count} fichiers du modpack...");
            var failed = await _dl.RunAsync(jobs, progress, CancellationToken.None, 8, continueOnError: true);
            // Un deuxième essai pour ceux qui ont raté (souvent l'antivirus qui bloquait le fichier)
            if (failed.Count > 0)
            {
                status?.Invoke($"Nouvel essai pour {failed.Count} fichier(s)...");
                await Task.Delay(1500);
                failed = await _dl.RunAsync(failed, null, CancellationToken.None, 2, continueOnError: true);
            }
            if (jobs.Count > 0 && failed.Count == jobs.Count)
                throw new Exception("Aucun fichier du modpack n'a pu être téléchargé. Vérifie ta connexion.");
            pack.FailedFiles = failed.Select(f => Path.GetFileName(f.Path)).ToList();

            // Dossiers "overrides" (configs, options, resource packs...)
            status?.Invoke("Copie des réglages du modpack...");
            foreach (var e in zip.Entries)
            {
                string rel = null;
                if (e.FullName.StartsWith("overrides/")) rel = e.FullName.Substring("overrides/".Length);
                else if (e.FullName.StartsWith("client-overrides/")) rel = e.FullName.Substring("client-overrides/".Length);
                if (string.IsNullOrEmpty(rel) || e.FullName.EndsWith("/")) continue;
                var target = SafeCombine(root, rel);
                if (target == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                e.ExtractToFile(target, true);
            }
            return pack;
        }

        /// <summary>Empêche un modpack d'écrire en dehors de son dossier (chemins avec "..").</summary>
        private static string SafeCombine(string root, string rel)
        {
            if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel)) return null;
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        private static string SafeId(string s)
        {
            var chars = (s ?? "pack").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            var id = new string(chars).Trim('-');
            return string.IsNullOrEmpty(id) ? "pack" : (id.Length > 40 ? id.Substring(0, 40) : id);
        }
    }
}
