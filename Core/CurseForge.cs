using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    /// <summary>
    /// Mods depuis CurseForge (API officielle, demande une clé gratuite : console.curseforge.com).
    /// </summary>
    public class CurseForge
    {
        private const string Api = "https://api.curseforge.com/v1";
        private const int MinecraftGameId = 432;
        private const int ModsClassId = 6;
        private const int FabricLoader = 4;

        private readonly HttpClient _http;
        private readonly Downloader _dl;
        public string ApiKey { get; set; }

        public CurseForge(HttpClient http)
        {
            _http = http;
            _dl = new Downloader(http);
        }

        /// <summary>Clé intégrée au .exe au moment du build (secret GitHub).</summary>
        public static string BuiltInKey
        {
            get
            {
                var attr = typeof(CurseForge).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                    .Cast<System.Reflection.AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "CurseForgeKey");
                return string.IsNullOrWhiteSpace(attr?.Value) ? null : attr.Value;
            }
        }

        /// <summary>Clé perso des paramètres si remplie, sinon la clé intégrée.</summary>
        private string EffectiveKey => string.IsNullOrWhiteSpace(ApiKey) ? BuiltInKey : ApiKey.Trim();

        public bool HasKey => !string.IsNullOrWhiteSpace(EffectiveKey);

        private async Task<JsonNode> GetAsync(string path)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Api + path);
            req.Headers.TryAddWithoutValidation("x-api-key", EffectiveKey);
            req.Headers.Add("Accept", "application/json");
            var res = await _http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                var detail = text.Length > 160 ? text.Substring(0, 160) : text;
                Log.Write($"CurseForge {(int)res.StatusCode} sur {path} : {text}");
                throw new CurseForgeException((int)res.StatusCode, detail);
            }
            return JsonNode.Parse(text);
        }

        // sort : relevance / downloads / follows / newest / updated (mêmes noms que Modrinth)
        private static int SortField(string sort) => sort switch
        {
            "downloads" => 6,   // TotalDownloads
            "follows" => 12,    // Rating
            "newest" => 11,     // ReleasedDate
            "updated" => 3,     // LastUpdated
            _ => 2              // Popularity
        };

        public async Task<(List<ModHit> hits, int total)> SearchAsync(string query, string mc, string sort,
            string categoryId, int offset)
        {
            var url = $"/mods/search?gameId={MinecraftGameId}&classId={ModsClassId}&gameVersion={mc}" +
                      $"&modLoaderType={FabricLoader}&sortField={SortField(sort)}&sortOrder=desc" +
                      $"&index={offset}&pageSize={ModManager.PageSize}" +
                      (string.IsNullOrWhiteSpace(query) ? "" : $"&searchFilter={Uri.EscapeDataString(query.Trim())}") +
                      (string.IsNullOrEmpty(categoryId) ? "" : $"&categoryId={categoryId}");
            JsonNode json;
            try { json = await GetAsync(url); }
            catch (CurseForgeException ex) when (ex.Status == 403 || ex.Status == 400)
            {
                // Deuxième essai avec le strict minimum (certaines clés n'acceptent pas tous les filtres)
                var simple = $"/mods/search?gameId={MinecraftGameId}&classId={ModsClassId}&gameVersion={mc}" +
                             $"&modLoaderType={FabricLoader}&index={offset}&pageSize={ModManager.PageSize}" +
                             (string.IsNullOrWhiteSpace(query) ? "" : $"&searchFilter={Uri.EscapeDataString(query.Trim())}");
                json = await GetAsync(simple);
            }
            var hits = json["data"].AsArray().Select(ToHit).ToList();
            int total = Math.Min(10000, (int?)json["pagination"]?["totalCount"] ?? hits.Count);
            return (hits, total);
        }

        private static ModHit ToHit(JsonNode m) => new ModHit
        {
            Source = "curseforge",
            ProjectId = ((int?)m["id"] ?? 0).ToString(),
            Slug = (string)m["slug"],
            Title = (string)m["name"],
            Author = (string)m["authors"]?.AsArray().FirstOrDefault()?["name"],
            Info = "par " + ((string)m["authors"]?.AsArray().FirstOrDefault()?["name"] ?? "?"),
            Description = (string)m["summary"],
            IconUrl = string.IsNullOrEmpty((string)m["logo"]?["thumbnailUrl"]) ? null : (string)m["logo"]["thumbnailUrl"],
            Downloads = ModManager.FormatDownloads((long?)m["downloadCount"] ?? 0),
            Follows = ModManager.FormatDownloads((long?)m["thumbsUpCount"] ?? 0),
            WebUrl = (string)m["links"]?["websiteUrl"],
            Tags = (m["categories"] as JsonArray ?? new JsonArray())
                .Select(c => (string)c["name"]).Where(n => n != null).Take(3).ToList()
        };

        public async Task<List<(string Id, string Name)>> GetCategoriesAsync()
        {
            var json = await GetAsync($"/categories?gameId={MinecraftGameId}&classId={ModsClassId}");
            return json["data"].AsArray()
                .Where(c => (bool?)c["isClass"] != true)
                .Select(c => (((int?)c["id"] ?? 0).ToString(), (string)c["name"]))
                .OrderBy(c => c.Item2)
                .ToList();
        }

        public async Task<ModProject> GetProjectAsync(string id)
        {
            var m = (await GetAsync($"/mods/{id}"))["data"];
            string body = "";
            try { body = (string)(await GetAsync($"/mods/{id}/description"))["data"] ?? ""; } catch { }
            var hit = ToHit(m);
            var p = new ModProject
            {
                Id = hit.ProjectId,
                Slug = hit.Slug,
                Title = hit.Title,
                Description = hit.Description,
                Body = body,
                IconUrl = hit.IconUrl,
                Downloads = hit.Downloads,
                Followers = hit.Follows,
                Updated = ModManager.Ago((string)m["dateModified"]),
                Published = ModManager.Ago((string)m["dateReleased"]),
                SourceUrl = (string)m["links"]?["sourceUrl"],
                IssuesUrl = (string)m["links"]?["issuesUrl"],
                WikiUrl = (string)m["links"]?["wikiUrl"],
                WebUrl = hit.WebUrl,
                Tags = hit.Tags
            };
            if (m["screenshots"] is JsonArray shots)
                p.Gallery = shots.Select(s => (string)s["thumbnailUrl"] ?? (string)s["url"])
                    .Where(u => !string.IsNullOrEmpty(u)).Take(4).ToList();
            return p;
        }

        private async Task<JsonArray> FilesAsync(string id, string mc) =>
            (await GetAsync($"/mods/{id}/files?gameVersion={mc}&modLoaderType={FabricLoader}&pageSize=20"))["data"].AsArray();

        public async Task<List<ModVersionInfo>> GetVersionsAsync(string id, string mc)
        {
            var files = await FilesAsync(id, mc);
            return files.Take(8).Select(f => new ModVersionInfo
            {
                Name = (string)f["displayName"],
                Number = (string)f["displayName"],
                Type = ((int?)f["releaseType"]) switch { 2 => "Bêta", 3 => "Alpha", _ => "Stable" },
                Date = ModManager.Ago((string)f["fileDate"]),
                Downloads = ModManager.FormatDownloads((long?)f["downloadCount"] ?? 0)
            }).ToList();
        }

        /// <summary>Télécharge le fichier dans le dossier mods + dépendances obligatoires. Renvoie le nom du fichier.</summary>
        public async Task<string> InstallAsync(string id, string mc, ModManager mods, HashSet<string> visiting = null)
        {
            visiting ??= new HashSet<string>();
            if (!visiting.Add(id)) return null;
            string key = "cf:" + id;
            if (mods.IsInstalled(key, mc)) return null;

            var files = await FilesAsync(id, mc);
            if (files.Count == 0) throw new Exception($"Ce mod n'existe pas pour Fabric {mc} sur CurseForge.");
            var file = files.FirstOrDefault(f => (int?)f["releaseType"] == 1) ?? files[0];

            var url = (string)file["downloadUrl"];
            if (string.IsNullOrEmpty(url))
                throw new Exception("L'auteur de ce mod interdit le téléchargement en dehors du site CurseForge.\n" +
                                    "Clique sur « Voir sur CurseForge » pour le télécharger à la main.");

            var fileName = (string)file["fileName"];
            string sha1 = null;
            if (file["hashes"] is JsonArray hashes)
                sha1 = (string)hashes.FirstOrDefault(h => (int?)h["algo"] == 1)?["value"];

            await _dl.DownloadOneAsync(new DownloadJob
            {
                Url = url,
                Path = Path.Combine(ModManager.ModsDir(mc), fileName),
                Sha1 = sha1
            }, CancellationToken.None);
            mods.RegisterInstalled(key, mc, fileName);

            if (file["dependencies"] is JsonArray deps)
                foreach (var d in deps)
                    if ((int?)d["relationType"] == 3 && d["modId"] != null)
                    {
                        try { await InstallAsync(((int)d["modId"]).ToString(), mc, mods, visiting); }
                        catch (Exception ex) { Log.Write("Dépendance CurseForge ignorée : " + ex.Message); }
                    }
            return fileName;
        }
    }

    public class CurseForgeException : Exception
    {
        public int Status { get; }
        public CurseForgeException(int status, string detail)
            : base(status == 401 || status == 403
                ? $"CurseForge refuse la clé (erreur {status}). {detail}"
                : $"Erreur CurseForge {status}. {detail}")
        {
            Status = status;
        }
    }
}
