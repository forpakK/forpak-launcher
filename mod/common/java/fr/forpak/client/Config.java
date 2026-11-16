package fr.forpak.client;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.nio.file.Files;
import java.nio.file.Path;

/** Réglages des modules, sauvegardés dans config/forpakclient.json */
public class Config {
    public boolean fps = true;
    public boolean cps = true;
    public boolean coords = true;
    public boolean keystrokes = true;
    public boolean armor = true;
    public boolean potions = true;
    public boolean pvpCounter = true;
    public boolean crystalOptimizer = true;
    public boolean zoom = true;
    public int zoomFov = 30;

    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static Path file() {
        return FabricLoader.getInstance().getConfigDir().resolve("forpakclient.json");
    }

    public static Config load() {
        try {
            Path f = file();
            if (Files.exists(f)) {
                Config c = GSON.fromJson(Files.readString(f), Config.class);
                if (c != null) return c;
            }
        } catch (Exception ignored) { }
        Config c = new Config();
        c.save();
        return c;
    }

    public void save() {
        try {
            Files.createDirectories(file().getParent());
            Files.writeString(file(), GSON.toJson(this));
        } catch (Exception ignored) { }
    }
}
