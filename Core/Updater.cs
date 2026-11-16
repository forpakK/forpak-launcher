using System.Diagnostics;
using System.Reflection;

namespace ForpakLauncher.Core
{
    /// <summary>Mises à jour automatiques depuis les Releases GitHub (tag "latest").</summary>
    public class Updater
    {
        // ⚠️ Mets ici ton pseudo GitHub / nom du dépôt
        public const string Repo = "forpakK/forpak-launcher";

        private readonly HttpClient _http;
        public Updater(HttpClient http) { _http = http; }

        public static string ReleaseFileUrl(string file) =>
            $"https://github.com/{Repo}/releases/download/latest/{file}";

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        /// <summary>Version publiée sur GitHub, ou null si introuvable.</summary>
        public async Task<string> GetRemoteVersionAsync()
        {
            try
            {
                var text = (await _http.GetStringAsync(ReleaseFileUrl("version.txt"))).Trim();
                return Version.TryParse(text, out _) ? text : null;
            }
            catch { return null; }
        }

        /// <summary>Notes de la dernière release (texte), ou null.</summary>
        public async Task<string> GetReleaseNotesAsync()
        {
            try
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(
                    await _http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/tags/latest"));
                return (string)json?["body"];
            }
            catch { return null; }
        }

        public static bool IsNewer(string remote) =>
            remote != null && Version.TryParse(remote, out var r) &&
            r > new Version(CurrentVersion.Major, CurrentVersion.Minor, Math.Max(0, CurrentVersion.Build));

        /// <summary>Télécharge le nouveau .exe, le remplace et relance le launcher.</summary>
        public async Task UpdateLauncherAsync(IProgress<double> progress)
        {
            var current = Environment.ProcessPath;
            var tmp = Path.Combine(Path.GetTempPath(), "VeloxClient_update.exe");

            using (var res = await _http.GetAsync(ReleaseFileUrl("VeloxClient.exe"), HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = File.Create(tmp);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            // Petit script qui attend la fermeture du launcher, remplace le .exe et le relance
            var bat = Path.Combine(Path.GetTempPath(), "velox_update.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                ":retry\r\n" +
                $"move /y \"{tmp}\" \"{current}\" >nul 2>&1\r\n" +
                "if errorlevel 1 (timeout /t 1 /nobreak >nul & goto retry)\r\n" +
                $"start \"\" \"{current}\"\r\n" +
                "del \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
