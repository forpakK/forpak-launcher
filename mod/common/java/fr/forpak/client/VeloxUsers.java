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
import java.util.HexFormat;
import java.util.Set;
import java.util.UUID;

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
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).build();

    public static void start() {
        if (API.isEmpty()) return;
        Thread t = new Thread(VeloxUsers::loop, "Velox users");
        t.setDaemon(true);
        t.start();
    }

    private static void loop() {
        long lastPing = 0;
        while (true) {
            try {
                MinecraftClient mc = MinecraftClient.getInstance();
                boolean inWorld = mc != null && mc.world != null;
                long now = System.currentTimeMillis();
                if (now - lastPing > 15 * 60 * 1000 && ping()) lastPing = now;
                if (inWorld) refresh();
                Thread.sleep(inWorld ? 60_000 : 30_000);
            } catch (InterruptedException e) {
                return;
            } catch (Throwable ignored) {
                try { Thread.sleep(60_000); } catch (InterruptedException e) { return; }
            }
        }
    }

    /** Se signale : on prouve à Mojang qu'on est bien ce compte, puis le serveur Velox vérifie. */
    private static boolean ping() throws Exception {
        if (ForpakClient.CONFIG != null && !ForpakClient.CONFIG.veloxIcon) return false;
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
                .POST(HttpRequest.BodyPublishers.ofString(ping.toString())).build(), HttpResponse.BodyHandlers.discarding());
        return r2.statusCode() == 200;
    }

    private static void refresh() throws Exception {
        var res = HTTP.send(HttpRequest.newBuilder(URI.create(API + "/online")).timeout(Duration.ofSeconds(10)).build(),
                HttpResponse.BodyHandlers.ofString());
        if (res.statusCode() != 200) return;
        Set<String> set = new HashSet<>();
        for (var e : JsonParser.parseString(res.body()).getAsJsonObject().getAsJsonArray("users"))
            set.add(e.getAsString().toLowerCase());
        online = set;
    }

    public static boolean isVelox(UUID uuid) {
        if (uuid == null || ForpakClient.CONFIG == null || !ForpakClient.CONFIG.veloxIcon) return false;
        MinecraftClient mc = MinecraftClient.getInstance();
        UUID me = mc.getSession().getUuidOrNull();
        if (uuid.equals(me) || (mc.player != null && uuid.equals(mc.player.getUuid()))) return true;
        return online.contains(uuid.toString().replace("-", "").toLowerCase());
    }

    /** Ajoute l'icône Velox devant un pseudo. */
    public static Text decorate(Text name) {
        MutableText out = Text.literal(ICON).setStyle(Style.EMPTY.withColor(Formatting.WHITE));
        return out.append(Text.literal(" ")).append(name);
    }
}
