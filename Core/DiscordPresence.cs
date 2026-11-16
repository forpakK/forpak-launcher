using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace ForpakLauncher.Core
{
    /// <summary>
    /// Statut Discord (« Joue à Velox Client ») via la connexion locale de l'appli Discord.
    /// Si Discord n'est pas ouvert, rien ne se passe.
    /// </summary>
    public sealed class DiscordPresence : IDisposable
    {
        public const string AppId = "1572012231779156029";
        private const string DownloadUrl = "https://github.com/forpakK/forpak-launcher/releases/latest";

        private NamedPipeClientStream _pipe;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public bool Enabled { get; set; } = true;

        public void Set(string details, string state, bool resetTimer = false)
        {
            if (resetTimer) _start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!Enabled) { Clear(); return; }
            var activity = new JsonObject
            {
                ["details"] = details,
                ["state"] = state,
                ["timestamps"] = new JsonObject { ["start"] = _start },
                ["assets"] = new JsonObject
                {
                    ["large_image"] = "velox",
                    ["large_text"] = "Velox Client " + Updater.CurrentVersionText
                },
                ["buttons"] = new JsonArray(new JsonObject { ["label"] = "Télécharger Velox Client", ["url"] = DownloadUrl })
            };
            _ = SendAsync(activity);
        }

        public void Clear() => _ = SendAsync(null);

        private async Task SendAsync(JsonObject activity)
        {
            await _gate.WaitAsync();
            try
            {
                if (_pipe == null || !_pipe.IsConnected)
                {
                    if (activity == null) return;
                    if (!await ConnectAsync()) return;
                }
                var payload = new JsonObject
                {
                    ["cmd"] = "SET_ACTIVITY",
                    ["args"] = new JsonObject
                    {
                        ["pid"] = Environment.ProcessId,
                        ["activity"] = activity
                    },
                    ["nonce"] = Guid.NewGuid().ToString()
                };
                await WriteAsync(1, payload.ToJsonString());
                await ReadAsync();
            }
            catch (Exception ex)
            {
                Log.Write("Discord : " + ex.Message);
                try { _pipe?.Dispose(); } catch { }
                _pipe = null;
            }
            finally { _gate.Release(); }
        }

        private async Task<bool> ConnectAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(300);
                    _pipe = pipe;
                    await WriteAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = AppId }.ToJsonString());
                    await ReadAsync();   // READY
                    return true;
                }
                catch
                {
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                }
            }
            return false;   // Discord n'est pas lancé
        }

        private async Task WriteAsync(int op, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            using var cts = new CancellationTokenSource(2000);
            await _pipe.WriteAsync(frame, cts.Token);
            await _pipe.FlushAsync(cts.Token);
        }

        private async Task ReadAsync()
        {
            using var cts = new CancellationTokenSource(2000);
            var header = new byte[8];
            await ReadExactly(header, cts.Token);
            int len = BitConverter.ToInt32(header, 4);
            if (len > 0 && len < 1_000_000) await ReadExactly(new byte[len], cts.Token);
        }

        private async Task ReadExactly(byte[] buf, CancellationToken ct)
        {
            int read = 0;
            while (read < buf.Length)
            {
                int n = await _pipe.ReadAsync(buf.AsMemory(read), ct);
                if (n <= 0) throw new IOException("Discord a fermé la connexion");
                read += n;
            }
        }

        public void Dispose()
        {
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }
    }
}
