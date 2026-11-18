using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    /// <summary>
    /// Serveur Velox (Cloudflare) : amis, invitations, classement.
    /// Le launcher se signale comme le jeu : preuve d'identité auprès de Mojang, puis jeton Velox.
    /// </summary>
    public class VeloxApi
    {
        public const string Base = "https://velox.fforpakk.workers.dev";
        private readonly HttpClient _http;
        private string _id, _token, _forUuid;

        public VeloxApi(HttpClient http) { _http = http; }

        public bool HasToken => _token != null;

        public async Task<bool> EnsureTokenAsync(McAccount acc)
        {
            if (acc == null || string.IsNullOrEmpty(acc.AccessToken)) return false;
            if (_token != null && _forUuid == acc.Uuid) return true;
            try
            {
                var serverId = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
                var join = new JsonObject
                {
                    ["accessToken"] = acc.AccessToken,
                    ["selectedProfile"] = acc.Uuid.Replace("-", ""),
                    ["serverId"] = serverId
                };
                using (var r1 = await _http.PostAsync("https://sessionserver.mojang.com/session/minecraft/join",
                           new StringContent(join.ToJsonString(), System.Text.Encoding.UTF8, "application/json")))
                    if (!r1.IsSuccessStatusCode) { Log.Write("Velox : join Mojang " + (int)r1.StatusCode); return false; }

                var ping = new JsonObject { ["name"] = acc.Name, ["serverId"] = serverId };
                using var r2 = await _http.PostAsync(Base + "/ping",
                    new StringContent(ping.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
                if (!r2.IsSuccessStatusCode) return false;
                using var doc = JsonDocument.Parse(await r2.Content.ReadAsStringAsync());
                _token = doc.RootElement.GetProperty("token").GetString();
                _id = acc.Uuid.Replace("-", "").ToLowerInvariant();
                _forUuid = acc.Uuid;
                return _token != null;
            }
            catch (Exception ex) { Log.Write("Velox : " + ex.Message); return false; }
        }

        public void Reset() { _token = null; _forUuid = null; }

        /// <summary>POST authentifié. Renvoie le JSON de la réponse, ou null.</summary>
        public async Task<JsonElement?> PostAsync(string path, JsonObject body)
        {
            if (_token == null) return null;
            body["id"] = _id;
            body["token"] = _token;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var r = await _http.PostAsync(Base + path,
                new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), cts.Token);
            if ((int)r.StatusCode == 403) { Reset(); return null; }
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(cts.Token));
            return doc.RootElement.Clone();
        }

        /// <summary>Fichier partagé avec le mod : amis, cape, mode streamer.</summary>
        public static void WriteShared(Settings s)
        {
            try
            {
                var o = new JsonObject
                {
                    ["friends"] = new JsonArray(s.Friends.Select(f => (JsonNode)f).ToArray()),
                    ["cape"] = s.Cape ?? "velox",
                    ["streamer"] = s.StreamerMode
                };
                File.WriteAllText(Path.Combine(Paths.Root, "velox_profile.json"), o.ToJsonString());
            }
            catch (Exception ex) { Log.Write("velox_profile : " + ex.Message); }
        }
    }
}
