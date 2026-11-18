package fr.forpak.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * Réglages partagés avec le launcher (%APPDATA%/.veloxclient/velox_profile.json) :
 * liste d'amis, cape choisie, mode streamer. Relu automatiquement quand le launcher le modifie.
 */
public final class Shared {
    private Shared() { }

    public static volatile List<String> friends = List.of();
    public static volatile String cape = "velox";
    public static volatile boolean streamer = false;
    private static long lastMtime = -1;

    private static Path file() {
        String appdata = System.getenv("APPDATA");
        if (appdata == null) appdata = System.getProperty("user.home");
        return Path.of(appdata, ".veloxclient", "velox_profile.json");
    }

    /** Relit le fichier s'il a changé (appelé toutes les 10 s, hors du thread du jeu). */
    public static void reload() {
        try {
            Path f = file();
            if (!Files.exists(f)) return;
            long m = Files.getLastModifiedTime(f).toMillis();
            if (m == lastMtime) return;
            lastMtime = m;
            JsonObject o = JsonParser.parseString(Files.readString(f)).getAsJsonObject();
            List<String> list = new ArrayList<>();
            if (o.has("friends")) for (var e : o.getAsJsonArray("friends")) list.add(e.getAsString());
            friends = List.copyOf(list);
            if (o.has("cape")) cape = o.get("cape").getAsString();
            if (o.has("streamer")) streamer = o.get("streamer").getAsBoolean();
        } catch (Exception ignored) { }
    }

    /** Mode streamer : réglage du launcher OU module en jeu. */
    public static boolean streamerOn() {
        return streamer || (ForpakClient.CONFIG != null && ForpakClient.CONFIG.streamerMode);
    }
}
