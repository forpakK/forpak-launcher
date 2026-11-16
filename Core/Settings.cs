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
        public bool CloseOnLaunch { get; set; } = false;
        public string LastVersion { get; set; } = "";

        // Compte (le refresh token est chiffré avec DPAPI, lié à ta session Windows)
        public string AccountName { get; set; } = "";
        public string AccountUuid { get; set; } = "";
        public string EncryptedRefreshToken { get; set; } = "";

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

        public string GetRefreshToken()
        {
            if (string.IsNullOrEmpty(EncryptedRefreshToken)) return null;
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(EncryptedRefreshToken), null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return null; }
        }

        public void SetRefreshToken(string token)
        {
            if (string.IsNullOrEmpty(token)) { EncryptedRefreshToken = ""; return; }
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            EncryptedRefreshToken = Convert.ToBase64String(data);
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
