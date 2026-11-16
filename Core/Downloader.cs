using System.Security.Cryptography;

namespace ForpakLauncher.Core
{
    public class DownloadJob
    {
        public string Url;
        public string Path;
        public string Sha1;   // optionnel
        public long Size;     // optionnel
    }

    /// <summary>Téléchargements parallèles avec vérif SHA1 et 3 essais.</summary>
    public class Downloader
    {
        private readonly HttpClient _http;
        public Downloader(HttpClient http) { _http = http; }

        public async Task RunAsync(IEnumerable<DownloadJob> jobs, IProgress<(int done, int total)> progress,
            CancellationToken ct, int parallel = 16)
        {
            // On ne télécharge que ce qui manque ou qui est abîmé
            var todo = jobs
                .GroupBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Where(j => !IsValid(j))
                .ToList();

            int done = 0, total = todo.Count;
            progress?.Report((0, total));
            if (total == 0) return;

            using var gate = new SemaphoreSlim(parallel);
            var tasks = todo.Select(async job =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await DownloadOneAsync(job, ct);
                    progress?.Report((Interlocked.Increment(ref done), total));
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        public async Task DownloadOneAsync(DownloadJob job, CancellationToken ct)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(job.Path));
                    var tmp = job.Path + ".part";
                    using (var res = await _http.GetAsync(job.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        res.EnsureSuccessStatusCode();
                        await using var fs = File.Create(tmp);
                        await res.Content.CopyToAsync(fs, ct);
                    }
                    if (!string.IsNullOrEmpty(job.Sha1) && !Sha1Of(tmp).Equals(job.Sha1, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("SHA1 invalide");
                    File.Move(tmp, job.Path, true);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(500 * attempt, ct);
                }
            }
            Log.Write($"Échec téléchargement {job.Url} : {last?.Message}");
            throw new Exception($"Impossible de télécharger {System.IO.Path.GetFileName(job.Path)} ({last?.Message})");
        }

        private static bool IsValid(DownloadJob j)
        {
            if (!File.Exists(j.Path)) return false;
            var len = new FileInfo(j.Path).Length;
            if (j.Size > 0 && len != j.Size) return false;
            if (len == 0) return false;
            // Vérif SHA1 complète seulement pour les petits fichiers importants (jars), sinon la taille suffit
            if (!string.IsNullOrEmpty(j.Sha1) && j.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                return Sha1Of(j.Path).Equals(j.Sha1, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public static string Sha1Of(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
        }
    }
}
