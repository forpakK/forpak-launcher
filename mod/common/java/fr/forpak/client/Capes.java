package fr.forpak.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.Identifier;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Capes Velox : la tienne (si le module est activé) + celles de tes potes,
 * listées dans capes.json sur le GitHub du launcher (uuid -> nom de cape).
 */
public final class Capes {
    private Capes() { }

    public static final Identifier VELOX = Identifier.of("veloxclient", "textures/cape/velox.png");
    private static final String LIST_URL =
            "https://raw.githubusercontent.com/forpakK/forpak-launcher/main/capes.json";
    private static final Map<String, Identifier> FRIENDS = new ConcurrentHashMap<>();

    /** Télécharge la liste des capes (en arrière-plan, ne bloque jamais le jeu). */
    public static void loadList() {
        Thread t = new Thread(() -> {
            try {
                HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).build();
                HttpResponse<String> res = http.send(HttpRequest.newBuilder(URI.create(LIST_URL))
                        .timeout(Duration.ofSeconds(10)).build(), HttpResponse.BodyHandlers.ofString());
                if (res.statusCode() != 200) return;
                JsonObject capes = JsonParser.parseString(res.body()).getAsJsonObject().getAsJsonObject("capes");
                if (capes == null) return;
                for (var e : capes.entrySet())
                    FRIENDS.put(e.getKey().replace("-", "").toLowerCase(), VELOX);
            } catch (Exception ignored) { }
        }, "Velox capes");
        t.setDaemon(true);
        t.start();
    }

    /** Cape à afficher pour ce joueur, ou null pour garder la cape normale. */
    public static Identifier capeFor(UUID uuid) {
        if (ForpakClient.CONFIG == null || !ForpakClient.CONFIG.cape) return null;
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.player != null && mc.player.getUuid().equals(uuid)) return VELOX;
        Identifier friend = FRIENDS.get(uuid.toString().replace("-", "").toLowerCase());
        if (friend != null) return friend;
        return VeloxUsers.isVelox(uuid) ? VELOX : null;   // tous les joueurs Velox ont la cape
    }
}
