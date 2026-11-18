package fr.forpak.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.client.MinecraftClient;
import net.minecraft.text.MutableText;
import net.minecraft.text.Style;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.security.SecureRandom;
import java.time.Duration;
import java.util.HashSet;
import fr.forpak.client.hud.Ui;
import java.util.HexFormat;
import java.util.Set;
import java.util.UUID;
import java.util.Map;
import java.util.HashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Qui joue avec Velox Client ? On se signale au serveur Velox (vérifié par Mojang)
 * et on récupère la liste des joueurs Velox en ligne pour afficher l'icône à côté du pseudo.
 */
public final class VeloxUsers {
    private VeloxUsers() { }

    /** Adresse du serveur Velox (Cloudflare). Vide = fonction désactivée. */
    public static final String API = "https://velox.fforpakk.workers.dev";

    /** Caractère de l'icône (dessiné par la police du mod : assets/minecraft/font/default.json). */
    public static final String ICON = "";

    private static volatile Set<String> online = Set.of();
    private static volatile Map<String, String> capes = Map.of();

    // Stats de la semaine à envoyer (classement entre amis)
    public static final AtomicInteger pendingKills = new AtomicInteger();
    public static final AtomicInteger pendingPops = new AtomicInteger();
    public static final AtomicInteger pendingCrystals = new AtomicInteger();

    // Invitation en attente (J pour rejoindre)
    private static volatile String inviteFrom, inviteServer;
    private static volatile long inviteAt;
    private static final Set<Long> seenInvites = new HashSet<>();
    private static final Map<String, Boolean> friendOnline = new HashMap<>();
    private static boolean friendsBaseline = false;
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).build();

    public static void start() {
        if (API.isEmpty()) return;
        Thread t = new Thread(VeloxUsers::loop, "Velox users");
        t.setDaemon(true);
        t.start();
    }

    private static volatile String token;      // jeton donné par le serveur Velox après vérification Mojang
    private static volatile String myId;
    private static String lastServer = null;
    private static long lastStatus = 0;

    private static void loop() {
        long lastPing = 0, lastRefresh = 0, lastFriends = 0, lastStats = 0;
        while (true) {
            try {
                MinecraftClient mc = MinecraftClient.getInstance();
                boolean inWorld = mc != null && mc.world != null;
                long now = System.currentTimeMillis();
                Shared.reload();
                if ((token == null || now - lastPing > 15 * 60 * 1000) && now - lastPing > 60_000) {
                    lastPing = now;
                    ping();
                }
                if (token != null) {
                    status(mc);
                    inbox();
                    if (now - lastStats > 60_000) { lastStats = now; sendStats(); }
                }
                if (now - lastFriends > 30_000) { lastFriends = now; friends(); }
                if (inWorld && now - lastRefresh > 60_000) { lastRefresh = now; refresh(); }
                Thread.sleep(10_000);
            } catch (InterruptedException e) {
                return;
            } catch (Throwable ignored) {
                try { Thread.sleep(30_000); } catch (InterruptedException e) { return; }
            }
        }
    }

    private static HttpResponse<String> post(String path, JsonObject b) throws Exception {
        return HTTP.send(HttpRequest.newBuilder(URI.create(API + path))
                .timeout(Duration.ofSeconds(10)).header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(b.toString())).build(), HttpResponse.BodyHandlers.ofString());
    }

    private static JsonObject authBody() {
        JsonObject b = authBody();
        b.addProperty("server", server);
        b.addProperty("cape", Shared.cape);
        var r = post("/status", b);
        if (r.statusCode() == 403) { token = null; return; }      // jeton périmé : on se re-signale
        lastServer = key;
        lastStatus = now;
    }

    /** Se signale : on prouve à Mojang qu'on est bien ce compte, puis le serveur Velox vérifie. */
    private static boolean ping() throws Exception {
        var session = MinecraftClient.getInstance().getSession();
        UUID uuid = session.getUuidOrNull();
        String token = session.getAccessToken();
        if (uuid == null || token == null || token.length() < 20) return false;

        byte[] rnd = new byte[20];
        new SecureRandom().nextBytes(rnd);
        String serverId = HexFormat.of().formatHex(rnd);

        JsonObject join = new JsonObject();
        join.addProperty("accessToken", token);
        join.addProperty("selectedProfile", uuid.toString().replace("-", ""));
        join.addProperty("serverId", serverId);
        var r1 = HTTP.send(HttpRequest.newBuilder(URI.create("https://sessionserver.mojang.com/session/minecraft/join"))
                .timeout(Duration.ofSeconds(10)).header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(join.toString())).build(), HttpResponse.BodyHandlers.discarding());
        if (r1.statusCode() / 100 != 2) return false;

        JsonObject ping = new JsonObject();
        ping.addProperty("name", session.getUsername());
        ping.addProperty("serverId", serverId);
        var r2 = HTTP.send(HttpRequest.newBuilder(URI.create(API + "/ping"))
                .timeout(Duration.ofSeconds(10)).header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(ping.toString())).build(), HttpResponse.BodyHandlers.ofString());
        if (r2.statusCode() != 200) return false;
        var t = JsonParser.parseString(r2.body()).getAsJsonObject().get("token");
        myId = uuid.toString().replace("-", "");
        token = t == null ? null : t.getAsString();
        lastServer = null;                                             // renvoie le statut tout de suite
        return true;
    }

    private static void refresh() throws Exception {
        var res = HTTP.send(HttpRequest.newBuilder(URI.create(API + "/online")).timeout(Duration.ofSeconds(10)).build(),
                HttpResponse.BodyHandlers.ofString());
        if (res.statusCode() != 200) return;
        Set<String> set = new HashSet<>();
        for (var e : JsonParser.parseString(res.body()).getAsJsonObject().getAsJsonArray("users"))
            set.add(e.getAsString().toLowerCase());
        online = set;
        Map<String, String> cp = new HashMap<>();
        JsonObject root = JsonParser.parseString(res.body()).getAsJsonObject();
        if (root.has("capes"))
            for (var e : root.getAsJsonObject("capes").entrySet()) cp.put(e.getKey(), e.getValue().getAsString());
        capes = cp;
    }

    public static boolean isVelox(UUID uuid) {
        if (uuid == null || ForpakClient.CONFIG == null || !ForpakClient.CONFIG.veloxIcon) return false;
        MinecraftClient mc = MinecraftClient.getInstance();
        UUID me = mc.getSession().getUuidOrNull();
        if (uuid.equals(me) || (mc.player != null && uuid.equals(mc.player.getUuid()))) return true;
        return online.contains(uuid.toString().replace("-", "").toLowerCase());
    }

    /** Cape choisie par un joueur Velox (null = cape Velox de base). */
    public static String capeOf(UUID uuid) {
        return capes.get(uuid.toString().replace("-", "").toLowerCase());
    }

    public static boolean isMe(UUID uuid) {
        MinecraftClient mc = MinecraftClient.getInstance();
        return uuid != null && (uuid.equals(mc.getSession().getUuidOrNull()) || (mc.player != null && uuid.equals(mc.player.getUuid())));
    }

    /** Ajoute l'icône Velox devant un pseudo. */
    public static Text decorate(Text name) {
        MutableText out = Text.literal(ICON).setStyle(Style.EMPTY.withColor(Formatting.WHITE));
        return out.append(Text.literal(" ")).append(name);
    }
}
