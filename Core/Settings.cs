using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForpakLauncher.Core
{
    public static class Paths
    {
        // Tout est rangé dans %APPDATA%\.veloxclient
        public static readonly string Root = InitRoot();

        private static string InitRoot()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var root = Path.Combine(appData, ".veloxclient");
            var old = Path.Combine(appData, ".forpaklauncher");   // ancien nom : on récupère tout
            try { if (!Directory.Exists(root) && Directory.Exists(old)) Directory.Move(old, root); } catch { }
            return root;
        }

        public static string Versions => Path.Combine(Root, "versions");
        public static string Libraries => Path.Combine(Root, "libraries");
        public static string Assets => Path.Combine(Root, "assets");
        public static string Runtime => Path.Combine(Root, "runtime");
        public static string SettingsFile => Path.Combine(Root, "launcher_settings.json");
        public static string LogFile => Path.Combine(Root, "launcher_log.txt");
    }

    public class Settings
    {
        // Vide = ID du launcher officiel. Mets ton ID Azure ici si tu en crées un un jour.
        public string ClientId { get; set; } = "";

        public int RamMb { get; set; } = 4096;
        public string JavaPath { get; set; } = "";      // vide = Java téléchargé automatiquement
        public string ExtraJvmArgs { get; set; } = "";
        public bool ShowSnapshots { get; set; } = false;
        public bool UseFabric { get; set; } = true;
        public bool ForpakClient { get; set; } = true;   // modules PvP (1.21.1 / 1.21.11)
        public bool PerformanceMode { get; set; } = true; // pack FPS (Sodium, Lithium...)
        public bool AnimatedBackground { get; set; } = true;
        public bool DiscordStatus { get; set; } = true;
        public bool LauncherBoost { get; set; } = true;   // optimisations sans mod (Java, priorité, GPU)
        public string BgWeather { get; set; } = "snow";   // snow / rain / none
        public string CurseForgeApiKey { get; set; } = "";
        public List<ModpackInstance> Modpacks { get; set; } = new();
        public List<ServerEntry> Servers { get; set; } = new();
        public List<string> Friends { get; set; } = new();
        public string Theme { get; set; } = "aurore";
        public string Cape { get; set; } = "velox";
        public bool StreamerMode { get; set; } = false;
        public bool CloseOnLaunch { get; set; } = false;
        public string LastVersion { get; set; } = "";
        public string LastPack { get; set; }

        // Compte (le refresh token est chiffré avec DPAPI, lié à ta session Windows)
        public string AccountName { get; set; } = "";
        public string AccountUuid { get; set; } = "";
        public string EncryptedRefreshToken { get; set; } = "";
        public string EncryptedMcToken { get; set; } = "";
        public DateTime McTokenExpiresAt { get; set; } = DateTime.MinValue;
        public string AccountXuid { get; set; } = "0";

        public static Settings Load()
        {
            try
            {
                if (File.Exists(Paths.SettingsFile))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings();
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Paths.SettingsFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public string GetRefreshToken() => Unprotect(EncryptedRefreshToken);
        public void SetRefreshToken(string token) => EncryptedRefreshToken = Protect(token);

        /// <summary>Compte complet gardé en mémoire (chiffré) : pas besoin de se reconnecter à chaque lancement.</summary>
        public McAccount LoadCachedAccount()
        {
            if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountUuid)) return null;
            return new McAccount
            {
                Name = AccountName,
                Uuid = AccountUuid,
                Xuid = AccountXuid ?? "0",
                AccessToken = Unprotect(EncryptedMcToken),
                ExpiresAt = McTokenExpiresAt,
                RefreshToken = GetRefreshToken()
            };
        }

        public void SaveAccount(McAccount a)
        {
            AccountName = a?.Name ?? "";
            AccountUuid = a?.Uuid ?? "";
            AccountXuid = a?.Xuid ?? "0";
            EncryptedMcToken = Protect(a?.AccessToken);
            McTokenExpiresAt = a?.ExpiresAt ?? DateTime.MinValue;
            SetRefreshToken(a?.RefreshToken);
            Save();
        }

        private static string Protect(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        private static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return null; }
        }
    }

    public static class Log
    {
        private static readonly object Lock = new();
        public static void Write(string msg)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Paths.Root);
                    File.AppendAllText(Paths.LogFile, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
