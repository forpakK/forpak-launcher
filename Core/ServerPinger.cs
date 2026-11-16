using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ForpakLauncher.Core
{
    public class ServerEntry
    {
        public string Name { get; set; }
        public string Address { get; set; }
    }

    public class ServerStatus
    {
        public bool Online;
        public int Players, MaxPlayers;
        public string Motd = "", Version = "";
        public long Ping;
        public byte[] Favicon;
    }

    /// <summary>Interroge un serveur Minecraft (protocole "Server List Ping") : joueurs, MOTD, ping.</summary>
    public static class ServerPinger
    {
        public static (string host, int port) Parse(string address)
        {
            address = (address ?? "").Trim();
            var i = address.LastIndexOf(':');
            if (i > 0 && int.TryParse(address.Substring(i + 1), out var port)) return (address.Substring(0, i), port);
            return (address, 25565);
        }

        public static async Task<ServerStatus> PingAsync(string address, int timeoutMs = 4000)
        {
            var (host, port) = Parse(address);
            using var cts = new CancellationTokenSource(timeoutMs);
            using var tcp = new TcpClient();
            var sw = Stopwatch.StartNew();
            await tcp.ConnectAsync(host, port, cts.Token);
            var stream = tcp.GetStream();

            // Handshake (état 1 = status) + demande de status
            var hs = new List<byte>();
            WriteVarInt(hs, 0x00);
            WriteVarInt(hs, 767);
            WriteString(hs, host);
            hs.Add((byte)(port >> 8)); hs.Add((byte)(port & 0xFF));
            WriteVarInt(hs, 1);
            await SendPacket(stream, hs, cts.Token);
            await SendPacket(stream, new List<byte> { 0x00 }, cts.Token);

            await ReadVarInt(stream, cts.Token);          // longueur
            await ReadVarInt(stream, cts.Token);          // id
            int len = await ReadVarInt(stream, cts.Token);
            var buf = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = await stream.ReadAsync(buf.AsMemory(read, len - read), cts.Token);
                if (n <= 0) throw new IOException("Connexion fermée");
                read += n;
            }
            sw.Stop();

            var json = JsonNode.Parse(Encoding.UTF8.GetString(buf));
            var st = new ServerStatus
            {
                Online = true,
                Ping = sw.ElapsedMilliseconds,
                Players = (int?)json["players"]?["online"] ?? 0,
                MaxPlayers = (int?)json["players"]?["max"] ?? 0,
                Version = (string)json["version"]?["name"] ?? "",
                Motd = CleanMotd(FlattenText(json["description"]))
            };
            var fav = (string)json["favicon"];
            if (fav != null && fav.Contains(","))
                try { st.Favicon = Convert.FromBase64String(fav.Substring(fav.IndexOf(',') + 1)); } catch { }
            return st;
        }

        private static string FlattenText(JsonNode n)
        {
            if (n == null) return "";
            if (n is JsonValue) return (string)n ?? "";
            var sb = new StringBuilder((string)n["text"] ?? "");
            if (n["extra"] is JsonArray extra) foreach (var e in extra) sb.Append(FlattenText(e));
            return sb.ToString();
        }

        private static string CleanMotd(string s) =>
            Regex.Replace(Regex.Replace(s ?? "", "§.", ""), @"\s+", " ").Trim();

        private static void WriteVarInt(List<byte> b, int value)
        {
            uint v = (uint)value;
            do
            {
                byte temp = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) temp |= 0x80;
                b.Add(temp);
            } while (v != 0);
        }

        private static void WriteString(List<byte> b, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteVarInt(b, bytes.Length);
            b.AddRange(bytes);
        }

        private static async Task SendPacket(NetworkStream s, List<byte> payload, CancellationToken ct)
        {
            var p = new List<byte>();
            WriteVarInt(p, payload.Count);
            p.AddRange(payload);
            await s.WriteAsync(p.ToArray(), ct);
        }

        private static async Task<int> ReadVarInt(NetworkStream s, CancellationToken ct)
        {
            int value = 0, shift = 0;
            var one = new byte[1];
            while (true)
            {
                int n = await s.ReadAsync(one.AsMemory(0, 1), ct);
                if (n <= 0) throw new IOException("Connexion fermée");
                value |= (one[0] & 0x7F) << shift;
                if ((one[0] & 0x80) == 0) return value;
                shift += 7;
                if (shift > 35) throw new IOException("VarInt trop long");
            }
        }
    }
}
