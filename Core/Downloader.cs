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

        /// <summary>
        /// Télécharge tout. Si continueOnError = true, un fichier qui échoue n'arrête pas les autres :
        /// la liste des fichiers ratés est renvoyée.
        /// </summary>
        public async Task<List<DownloadJob>> RunAsync(IEnumerable<DownloadJob> jobs, IProgress<(int done, int total)> progress,
            CancellationToken ct, int parallel = 16, bool continueOnError = false)
        {
            var failed = new List<DownloadJob>();
            // On ne télécharge que ce qui manque ou qui est abîmé
            var todo = jobs
                .GroupBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Where(j => !IsValid(j))
                .ToList();

            int done = 0, total = todo.Count;
            progress?.Report((0, total));
            if (total == 0) return failed;

            using var gate = new SemaphoreSlim(parallel);
            var tasks = todo.Select(async job =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await DownloadOneAsync(job, ct);
                }
                catch (Exception) when (continueOnError)
                {
                    lock (failed) failed.Add(job);
                }
                finally
                {
                    progress?.Report((Interlocked.Increment(ref done), total));
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            return failed;
        }

        public async Task DownloadOneAsync(DownloadJob job, CancellationToken ct)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(job.Path));
                    // Nom temporaire unique : un vieux .part bloqué (antivirus...) ne gêne plus
                    var tmp = job.Path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part";
                    try
                    {
                        using (var res = await _http.GetAsync(job.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            res.EnsureSuccessStatusCode();
                            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                            await res.Content.CopyToAsync(fs, ct);
                        }
                        if (!string.IsNullOrEmpty(job.Sha1) && !Sha1Of(tmp).Equals(job.Sha1, StringComparison.OrdinalIgnoreCase))
                            throw new Exception("SHA1 invalide");
                        await MoveWithRetryAsync(tmp, job.Path, ct);
                        return;
                    }
                    finally
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
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

        /// <summary>
        /// L'antivirus analyse souvent un fichier qui vient d'être écrit et le bloque quelques instants :
        /// on réessaie le déplacement, puis on tente une copie.
        /// </summary>
        private static async Task MoveWithRetryAsync(string from, string to, CancellationToken ct)
        {
            for (int i = 0; i < 8; i++)
            {
                try { File.Move(from, to, true); return; }
                catch (IOException) when (i < 7) { await Task.Delay(250 * (i + 1), ct); }
                catch (UnauthorizedAccessException) when (i < 7) { await Task.Delay(250 * (i + 1), ct); }
            }
            File.Copy(from, to, true);
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
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
        }
    }
}
