using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    public class McAccount
    {
        public string Name;
        public string Uuid;
        public string AccessToken;
        public string Xuid = "0";
        public string RefreshToken;
        /// <summary>Le jeton Minecraft expire (≈ 24 h).</summary>
        public DateTime ExpiresAt = DateTime.MinValue;
        public bool TokenValid(TimeSpan margin) =>
            !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow + margin < ExpiresAt;
    }

    /// <summary>La session Microsoft n'est plus valable : il faut se reconnecter.</summary>
    public class SessionExpiredException : Exception
    {
        public SessionExpiredException() : base("Session expirée, reconnecte-toi.") { }
    }

    public class DeviceCodeInfo
    {
        public string UserCode;
        public string VerificationUri;
        public string DeviceCode;
        public int Interval;
        public int ExpiresIn;
    }

    /// <summary>
    /// Connexion Microsoft -> Xbox Live -> XSTS -> Minecraft (flux "device code").
    /// </summary>
    public class MicrosoftAuth
    {
        private const string Scope = "XboxLive.signin offline_access";

        // Client ID du launcher officiel Minecraft (utilisé si tu n'as pas le tien)
        public const string OfficialClientId = "00000000402B5328";
        private const string LiveRedirect = "https://login.live.com/oauth20_desktop.srf";

        private readonly HttpClient _http;
        private readonly string _clientId;
        private readonly bool _live;          // true = endpoints login.live.com (ID officiel)
        private readonly string _deviceUrl;
        private readonly string _tokenUrl;

        public MicrosoftAuth(HttpClient http, string clientId)
        {
            _http = http;
            _live = string.IsNullOrWhiteSpace(clientId);
            _clientId = _live ? OfficialClientId : clientId.Trim();
            if (_live)
            {
                _deviceUrl = "https://login.live.com/oauth20_connect.srf";
                _tokenUrl = "https://login.live.com/oauth20_token.srf";
            }
            else
            {
                _deviceUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
                _tokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
            }
        }

        public string EffectiveClientId => _clientId;

        public async Task<DeviceCodeInfo> StartDeviceCodeAsync()
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scope"] = Scope
            };
            if (_live)
            {
                form["redirect_uri"] = LiveRedirect;
                form["response_type"] = "device_code";
            }
            var res = await _http.PostAsync(_deviceUrl, new FormUrlEncodedContent(form));
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync());
            if (!res.IsSuccessStatusCode || json?["device_code"] == null)
                throw new Exception("Microsoft a refusé la demande : " +
                    ((string)json?["error_description"] ?? (string)json?["error"]));

            return new DeviceCodeInfo
            {
                UserCode = (string)json["user_code"],
                VerificationUri = (string)json["verification_uri"] ?? (string)json["verification_url"]
                                  ?? "https://www.microsoft.com/link",
                DeviceCode = (string)json["device_code"],
                Interval = (int?)json["interval"] ?? 5,
                ExpiresIn = (int?)json["expires_in"] ?? 900
            };
        }

        public async Task<McAccount> WaitForDeviceCodeAsync(DeviceCodeInfo info, CancellationToken ct)
        {
            var interval = info.Interval;
            var deadline = DateTime.UtcNow.AddSeconds(info.ExpiresIn);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                // Pas de redirect_uri ici : avec login.live.com ça bloque en "authorization_pending"
                var res = await _http.PostAsync(_tokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = _clientId,
                    ["device_code"] = info.DeviceCode
                }), ct);
                var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
                if (res.IsSuccessStatusCode)
                    return await FinishAsync((string)json["access_token"], (string)json["refresh_token"]);

                var err = (string)json?["error"];
                if (err == "authorization_pending") continue;
                if (err == "slow_down") { interval += 5; continue; }
                throw new Exception("Connexion annulée ou expirée (" + err + ").");
            }
            throw new Exception("Le code a expiré, recommence.");
        }

        public async Task<McAccount> RefreshAsync(string refreshToken)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _clientId,
                ["refresh_token"] = refreshToken,
                ["scope"] = Scope
            };
            if (_live) form["redirect_uri"] = LiveRedirect;
            var res = await _http.PostAsync(_tokenUrl, new FormUrlEncodedContent(form));
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                // invalid_grant = le jeton n'est vraiment plus bon. Sinon (serveur en panne...) on réessaiera plus tard.
                if (body.Contains("invalid_grant") || body.Contains("interaction_required"))
                    throw new SessionExpiredException();
                throw new Exception($"Microsoft ne répond pas ({(int)res.StatusCode}), réessaie dans un instant.");
            }
            var json = JsonNode.Parse(body);
            return await FinishAsync((string)json["access_token"], (string)json["refresh_token"] ?? refreshToken);
        }

        private async Task<McAccount> FinishAsync(string msToken, string refreshToken)
        {
            // 1) Xbox Live
            var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = "d=" + msToken
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            });
            var xblToken = (string)xbl["Token"];
            var uhs = (string)xbl["DisplayClaims"]["xui"][0]["uhs"];

            // 2) XSTS
            var xstsRes = await _http.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", JsonContent(new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = new JsonArray(xblToken)
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            }));
            var xstsBody = await xstsRes.Content.ReadAsStringAsync();
            if (!xstsRes.IsSuccessStatusCode)
            {
                var xerr = string.IsNullOrWhiteSpace(xstsBody) ? "" : JsonNode.Parse(xstsBody)?["XErr"]?.ToString();
                throw new Exception(xerr switch
                {
                    "2148916233" => "Ce compte Microsoft n'a pas de profil Xbox. Crée-en un sur xbox.com puis réessaie.",
                    "2148916235" => "Xbox Live n'est pas disponible dans ton pays.",
                    "2148916238" => "Compte enfant : il doit être ajouté à une famille Microsoft.",
                    _ => "Erreur Xbox (XSTS) " + xerr
                });
            }
            var xsts = JsonNode.Parse(xstsBody);
            var xstsToken = (string)xsts["Token"];

            // 3) Minecraft
            var mcRes = await _http.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
                JsonContent(new JsonObject { ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}" }));
            var mcBody = await mcRes.Content.ReadAsStringAsync();
            if (!mcRes.IsSuccessStatusCode)
            {
                if ((int)mcRes.StatusCode == 403)
                    throw new Exception("Ton Client ID Azure n'est pas encore autorisé par Mojang (voir README, étape « demande d'accès »).");
                throw new Exception("Erreur Minecraft : " + mcBody);
            }
            var mcJson = JsonNode.Parse(mcBody);
            var mcToken = (string)mcJson["access_token"];
            var mcExpires = DateTime.UtcNow.AddSeconds((int?)mcJson["expires_in"] ?? 86400);

            // 4) Profil (vérifie que le jeu est acheté)
            var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);
            var profRes = await _http.SendAsync(req);
            if ((int)profRes.StatusCode == 404)
                throw new Exception("Ce compte ne possède pas Minecraft Java Edition.");
            profRes.EnsureSuccessStatusCode();
            var prof = JsonNode.Parse(await profRes.Content.ReadAsStringAsync());

            return new McAccount
            {
                Name = (string)prof["name"],
                Uuid = (string)prof["id"],
                AccessToken = mcToken,
                ExpiresAt = mcExpires,
                Xuid = (string)xsts["DisplayClaims"]?["xui"]?[0]?["xid"] ?? "0",
                RefreshToken = refreshToken
            };
        }

        private async Task<JsonNode> PostJsonAsync(string url, JsonObject body)
        {
            var res = await _http.PostAsync(url, JsonContent(body));
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception($"Erreur {(int)res.StatusCode} sur {new Uri(url).Host}");
            return JsonNode.Parse(text);
        }

        private static StringContent JsonContent(JsonObject o) =>
            new StringContent(o.ToJsonString(), Encoding.UTF8, "application/json");
    }
}
