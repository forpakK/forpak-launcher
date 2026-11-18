package fr.forpak.client;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

/** Réglages, sauvegardés dans config/veloxclient.json */
public class Config {
    /** Modules HUD activés (id -> on/off) */
    public Map<String, Boolean> enabled = new HashMap<>();
    /** Position des éléments du HUD (id -> {x, y} entre 0 et 1) */
    public Map<String, float[]> pos = new HashMap<>();

    /** Style de chaque élément du HUD (taille, fond, couleur) */
    public Map<String, ElementStyle> styles = new HashMap<>();

    public boolean crystalOptimizer = true;
    public boolean anchorOptimizer = true;
    public boolean customMenu = true;
    public boolean cape = true;
    public boolean veloxIcon = true;
    public boolean shareServer = true;
    public int killEffect = 1;           // 0 aucun, 1 éclair, 2 flocons, 3 étincelles, 4 cœurs, 5 flammes
    public double zoomLevel = 4.0;       // zoom de départ (x4)
    public boolean streamerMode = false; // cache coordonnées, pseudo et serveur
    public Map<String, String> serverProfiles = new HashMap<>(); // adresse du serveur -> profil de HUD  // tes amis Velox voient sur quel serveur tu joues
    public boolean lowHpWarning = true; // bords rouges quand la vie est basse    // icône Velox à côté du pseudo des joueurs Velox
    public boolean freelook = true;
    public int weatherMode = 0;       // 0 normale, 1 clair, 2 pluie, 3 neige
    public int lastWeatherMode = 1;   // pour réactiver le module avec le dernier choix
    public boolean noParticles = false;
    public boolean hitColorEnabled = false;
    public int hitColor = 0xFF3EE6A8;

    // Viseur perso
    public boolean crosshair = false;
    public int crosshairStyle = 0;
    public int crosshairSize = 4;
    public int crosshairGap = 2;
    public int crosshairColor = 0xFF3EE6A8;
    public boolean crosshairOutline = true;

    // Profils de HUD (PvP / Survie / Build)
    public Map<String, Profile> profiles = new HashMap<>();

    public static class Profile {
        public Map<String, Boolean> enabled = new HashMap<>();
        public Map<String, float[]> pos = new HashMap<>();
        public Map<String, ElementStyle> styles = new HashMap<>();
    }

    public void saveProfile(String name) {
        profiles.put(name, GSON.fromJson(GSON.toJson(snapshot()), Profile.class));
        save();
    }

    public boolean loadProfile(String name) {
        Profile p = profiles.get(name);
        if (p == null) return false;
        Profile copy = GSON.fromJson(GSON.toJson(p), Profile.class);
        enabled = copy.enabled != null ? copy.enabled : new HashMap<>();
        pos = copy.pos != null ? copy.pos : new HashMap<>();
        styles = copy.styles != null ? copy.styles : new HashMap<>();
        save();
        return true;
    }

    private Profile snapshot() {
        Profile p = new Profile();
        p.enabled = enabled; p.pos = pos; p.styles = styles;
        return p;
    }
    public boolean zoom = true;
    public int zoomFov = 30;
    public boolean toggleSprintModule = true;
    public boolean toggleSneakModule = true;

    // État des toggles (sauvegardé pour garder le sprint entre deux parties)
    public boolean sprintToggled = false;
    public boolean sneakToggled = false;

    public static class ElementStyle {
        public float scale = 1f;
        public boolean background = true;
        public int color = 0xFF3EE6A8;
    }

    public ElementStyle style(String id) {
        return styles.computeIfAbsent(id, k -> new ElementStyle());
    }

    public boolean isOn(String id, boolean def) {
        Boolean b = enabled.get(id);
        return b == null ? def : b;
    }

    // ------------------------------------------------------------------

    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

    private static Path file() {
        return FabricLoader.getInstance().getConfigDir().resolve("veloxclient.json");
    }

    private static Path legacyFile() {
        return FabricLoader.getInstance().getConfigDir().resolve("forpakclient.json");
    }

    public static Config load() {
        try {
            Path f = file();
            if (!Files.exists(f) && Files.exists(legacyFile())) f = legacyFile();
            if (Files.exists(f)) {
                Config c = GSON.fromJson(Files.readString(f), Config.class);
                if (c != null) {
                    if (c.enabled == null) c.enabled = new HashMap<>();
                    if (c.pos == null) c.pos = new HashMap<>();
                    if (c.styles == null) c.styles = new HashMap<>();
                    if (c.profiles == null) c.profiles = new HashMap<>();
                    if (c.serverProfiles == null) c.serverProfiles = new HashMap<>();
                    return c;
                }
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
