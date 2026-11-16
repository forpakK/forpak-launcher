using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    public class ModHit
    {
        public string ProjectId { get; set; }
        public string Slug { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Info { get; set; }
        public string IconUrl { get; set; }
        public string Downloads { get; set; }
        public string Follows { get; set; }
        public string Updated { get; set; }
        public List<string> Tags { get; set; } = new();
        public string InstallLabel { get; set; } = "Installer";
    }

    public class ModProject
    {
        public string Id, Slug, Title, Description, Body, IconUrl, Downloads, Followers, Updated, Published, License;
        public string SourceUrl, IssuesUrl, DiscordUrl, WikiUrl;
        public List<string> Tags = new();
        public List<string> Gallery = new();
    }

    public class ModVersionInfo
    {
        public string Name { get; set; }
        public string Number { get; set; }
        public string Type { get; set; }
        public string Date { get; set; }
        public string Downloads { get; set; }
        public string GameVersions { get; set; }
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

        public const int PageSize = 20;

        /// <summary>Recherche Modrinth. sort = relevance / downloads / follows / newest / updated.</summary>
        public async Task<(List<ModHit> hits, int total)> SearchAsync(string query, string mc,
            string sort = "relevance", string category = null, int offset = 0)
        {
            var facets = $"[[\"project_type:mod\"],[\"categories:fabric\"],[\"versions:{mc}\"]" +
                         (string.IsNullOrEmpty(category) ? "" : $",[\"categories:{category}\"]") + "]";
            var url = $"{Api}/search?limit={PageSize}&offset={offset}&index={sort}" +
                      $"&query={Uri.EscapeDataString(query ?? "")}&facets={Uri.EscapeDataString(facets)}";
            var json = JsonNode.Parse(await _http.GetStringAsync(url));
            var hits = json["hits"].AsArray().Select(h => new ModHit
            {
                ProjectId = (string)h["project_id"],
                Slug = (string)h["slug"],
                Title = (string)h["title"],
                Author = (string)h["author"],
                Description = (string)h["description"],
                IconUrl = string.IsNullOrEmpty((string)h["icon_url"]) ? null : (string)h["icon_url"],
                Downloads = FormatDownloads((long?)h["downloads"] ?? 0),
                Follows = FormatDownloads((long?)h["follows"] ?? 0),
                Updated = Ago((string)h["date_modified"]),
                Info = $"par {(string)h["author"]}",
                Tags = (h["display_categories"] as JsonArray ?? h["categories"] as JsonArray ?? new JsonArray())
                    .Select(c => CategoryName((string)c)).Where(c => c != null).Distinct().Take(3).ToList()
            }).ToList();
            return (hits, (int?)json["total_hits"] ?? hits.Count);
        }

        public async Task<ModProject> GetProjectAsync(string idOrSlug)
        {
            var j = JsonNode.Parse(await _http.GetStringAsync($"{Api}/project/{idOrSlug}"));
            var p = new ModProject
            {
                Id = (string)j["id"],
                Slug = (string)j["slug"],
                Title = (string)j["title"],
                Description = (string)j["description"],
                Body = (string)j["body"] ?? "",
                IconUrl = string.IsNullOrEmpty((string)j["icon_url"]) ? null : (string)j["icon_url"],
                Downloads = FormatDownloads((long?)j["downloads"] ?? 0),
                Followers = FormatDownloads((long?)j["followers"] ?? 0),
                Updated = Ago((string)j["updated"]),
                Published = Ago((string)j["published"]),
                License = (string)j["license"]?["id"],
                SourceUrl = (string)j["source_url"],
                IssuesUrl = (string)j["issues_url"],
                DiscordUrl = (string)j["discord_url"],
                WikiUrl = (string)j["wiki_url"]
            };
            if (j["categories"] is JsonArray cats)
                p.Tags = cats.Select(c => CategoryName((string)c)).Where(c => c != null).ToList();
            if (j["gallery"] is JsonArray gal)
                p.Gallery = gal.OrderByDescending(g => (bool?)g["featured"] == true)
                    .Select(g => (string)g["url"]).Where(u => !string.IsNullOrEmpty(u)).Take(4).ToList();
            return p;
        }

        public async Task<List<ModVersionInfo>> GetVersionsAsync(string projectId, string mc)
        {
            var url = $"{Api}/project/{projectId}/version?loaders={Uri.EscapeDataString("[\"fabric\"]")}" +
                      $"&game_versions={Uri.EscapeDataString($"[\"{mc}\"]")}";
            var arr = JsonNode.Parse(await _http.GetStringAsync(url)).AsArray();
            return arr.Take(8).Select(v => new ModVersionInfo
            {
                Name = (string)v["name"],
                Number = (string)v["version_number"],
                Type = ((string)v["version_type"]) switch { "beta" => "Bêta", "alpha" => "Alpha", _ => "Stable" },
                Date = Ago((string)v["date_published"]),
                Downloads = FormatDownloads((long?)v["downloads"] ?? 0),
                GameVersions = string.Join(", ", (v["game_versions"] as JsonArray ?? new JsonArray())
                    .Select(g => (string)g).TakeLast(3))
            }).ToList();
        }

        public bool IsInstalled(string projectId, string mc) =>
            LoadIndex(mc).TryGetValue(projectId, out var f) && File.Exists(Path.Combine(ModsDir(mc), f));

        // Catégories Modrinth -> français
        public static readonly (string Slug, string Name)[] Categories =
        {
            ("", "Tout"), ("optimization", "Optimisation"), ("utility", "Utilitaire"), ("decoration", "Décoration"),
            ("adventure", "Aventure"), ("technology", "Technologie"), ("magic", "Magie"), ("worldgen", "Monde"),
            ("equipment", "Équipement"), ("social", "Social"), ("library", "Bibliothèque")
        };

        private static string CategoryName(string slug) => slug switch
        {
            "optimization" => "Optimisation", "utility" => "Utilitaire", "decoration" => "Décoration",
            "adventure" => "Aventure", "technology" => "Technologie", "magic" => "Magie", "worldgen" => "Monde",
            "equipment" => "Équipement", "social" => "Social", "library" => "Bibliothèque", "storage" => "Stockage",
            "food" => "Nourriture", "mobs" => "Créatures", "transportation" => "Transport", "management" => "Gestion",
            "game-mechanics" => "Mécaniques", "cursed" => "Maudit", "economy" => "Économie", "minigame" => "Mini-jeu",
            "fabric" or "forge" or "neoforge" or "quilt" or "datapack" => null,
            null => null,
            _ => char.ToUpper(slug[0]) + slug.Substring(1)
        };

        private static string Ago(string iso)
        {
            if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)) return "";
            var span = DateTime.UtcNow - d.ToUniversalTime();
            if (span.TotalDays >= 365) return $"il y a {(int)(span.TotalDays / 365)} an{((int)(span.TotalDays / 365) > 1 ? "s" : "")}";
            if (span.TotalDays >= 30) return $"il y a {(int)(span.TotalDays / 30)} mois";
            if (span.TotalDays >= 1) return $"il y a {(int)span.TotalDays} j";
            if (span.TotalHours >= 1) return $"il y a {(int)span.TotalHours} h";
            return "à l'instant";
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
