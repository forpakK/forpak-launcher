package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

import java.util.ArrayList;
import java.util.List;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;
import java.util.function.Function;

/**
 * Menu des modules façon Lunar, épuré et adapté à toutes les tailles d'interface.
 * Catégories à gauche, cartes avec interrupteur à droite.
 * Clic sur la carte = activer · engrenage (ou clic droit) = réglages.
 */
public class ModulesScreen extends Screen {

    private record Module(String name, String desc, int category, BooleanSupplier get, Consumer<Boolean> set,
                          Function<Screen, Screen> settings) { }

    private static final String[] CATS = {"HUD", "PvP", "Visuel", "Tout"};
    private static int cat = 0;      // gardé entre deux ouvertures
    private int page = 0;

    private final Screen parent;
    private final List<Module> modules = new ArrayList<>();
    private final Clicks clicks = new Clicks();

    private static final int CARD_W = 100, CARD_H = 44, GAP = 5, SIDE_W = 84;

    public ModulesScreen(Screen parent) {
        super(Text.literal("Modules"));
        this.parent = parent;
        Config c = ForpakClient.CONFIG;
        for (HudElement e : Hud.ELEMENTS)
            modules.add(new Module(e.name, "Élément du HUD", 0, e::isEnabled, e::setEnabled,
                    p -> new ModuleSettingsScreen(p, e)));

        modules.add(new Module("Crystal Optimizer", "Crystal PvP", 1, () -> c.crystalOptimizer, v -> { c.crystalOptimizer = v; c.save(); }, null));
        modules.add(new Module("Anchor Optimizer", "Anchor PvP", 1, () -> c.anchorOptimizer, v -> { c.anchorOptimizer = v; c.save(); }, null));
        modules.add(new Module("ToggleSprint", "Touche G", 1, () -> c.toggleSprintModule, v -> { c.toggleSprintModule = v; c.save(); }, null));
        modules.add(new Module("ToggleSneak", "Touche V", 1, () -> c.toggleSneakModule, v -> { c.toggleSneakModule = v; c.save(); }, null));
        modules.add(new Module("Alerte vie basse", "Bords rouges", 1, () -> c.lowHpWarning, v -> { c.lowHpWarning = v; c.save(); }, null));
        modules.add(new Module("Zoom", "Maintenir C", 1, () -> c.zoom, v -> { c.zoom = v; c.save(); }, null));
        modules.add(new Module("Freelook", "Maintenir Alt", 1, () -> c.freelook, v -> { c.freelook = v; c.save(); }, null));

        modules.add(new Module("Viseur perso", "Forme et couleur", 2, () -> c.crosshair, v -> { c.crosshair = v; c.save(); }, CrosshairScreen::new));
        modules.add(new Module("Hit color", "Couleur des coups", 2, () -> c.hitColorEnabled, v -> { c.hitColorEnabled = v; c.save(); }, HitColorScreen::new));
        modules.add(new Module("Météo perso", "Clair, pluie, neige", 2, () -> c.weatherMode != 0, v -> {
            if (v) c.weatherMode = c.lastWeatherMode == 0 ? 1 : c.lastWeatherMode;
            else { if (c.weatherMode != 0) c.lastWeatherMode = c.weatherMode; c.weatherMode = 0; }
            c.save();
        }, WeatherScreen::new));
        modules.add(new Module("Sans particules", "Plus de FPS", 2, () -> c.noParticles, v -> { c.noParticles = v; c.save(); }, null));
        modules.add(new Module("Icône Velox", "À côté du pseudo", 2, () -> c.veloxIcon, v -> { c.veloxIcon = v; c.save(); }, null));
        modules.add(new Module("Cape Velox", "Ta cape", 2, () -> c.cape, v -> { c.cape = v; c.save(); }, null));
        modules.add(new Module("Menu Velox", "Écran titre perso", 2, () -> c.customMenu, v -> { c.customMenu = v; c.save(); }, null));
    }

    private List<Module> shown() {
        if (cat == 3) return modules;
        List<Module> out = new ArrayList<>();
        for (Module m : modules) if (m.category() == cat) out.add(m);
        return out;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        clicks.poll(client);
        ctx.fill(0, 0, width, height, 0xA0050810);

        // ---- Panneau : s'adapte à l'écran (même en taille d'interface 5)
        int pw = Math.max(200, Math.min(width - 12, SIDE_W + 20 + 4 * CARD_W + 3 * GAP));
        int ph = Math.max(150, Math.min(height - 12, 290));
        int px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, 0xF20A0F1C);
        Ui.outline(ctx, px, py, pw, ph, 0xFF1F2A3D);

        // ---- Colonne de gauche : logo + catégories
        ctx.fill(px + 1, py + 1, px + SIDE_W, py + ph - 1, 0xFF0D1526);
        ctx.fill(px + SIDE_W, py + 1, px + SIDE_W + 1, py + ph - 1, 0xFF1F2A3D);
        ctx.drawTextWithShadow(textRenderer, "VELOX", px + 10, py + 10, Ui.WHITE);
        ctx.drawTextWithShadow(textRenderer, ".", px + 10 + textRenderer.getWidth("VELOX"), py + 10, Ui.ACCENT);
        ctx.drawTextWithShadow(textRenderer, "Modules", px + 10, py + 21, 0xFF7F8AA0);

        int cy = py + 38;
        for (int i = 0; i < CATS.length; i++) {
            int bx = px + 5, bw = SIDE_W - 10;
            boolean on = cat == i, hov = Ui.inside(mouseX, mouseY, bx, cy, bw, 18);
            if (on) Ui.rect(ctx, bx, cy, bw, 18, 0xFF163D33);
            else if (hov) Ui.rect(ctx, bx, cy, bw, 18, 0xFF141C30);
            if (on) ctx.fill(bx, cy + 4, bx + 2, cy + 14, Ui.ACCENT);
            ctx.drawTextWithShadow(textRenderer, CATS[i], bx + 9, cy + 5, on ? Ui.WHITE : Ui.GREY);
            if (hov && clicks.left) { cat = i; page = 0; }
            cy += 21;
        }

        int by = py + ph - 24;
        if (Ui.button(ctx, textRenderer, "Retour", px + 5, by, SIDE_W - 10, 18, mouseX, mouseY, clicks.left, false)) { close(); return; }
        if (by - 22 > cy && Ui.button(ctx, textRenderer, "HUD", px + 5, by - 22, SIDE_W - 10, 18, mouseX, mouseY, clicks.left, true)) {
            client.setScreen(new HudEditorScreen(this));
            return;
        }

        // ---- Zone des cartes (colonnes et lignes calculées selon la place)
        int ax = px + SIDE_W + 10, ay = py + 10;
        int aw = px + pw - 10 - ax, ah = py + ph - 30 - ay;
        int cols = Math.max(1, (aw + GAP) / (CARD_W + GAP));
        int rows = Math.max(1, (ah + GAP) / (CARD_H + GAP));
        int perPage = cols * rows;

        List<Module> list = shown();
        int pages = Math.max(1, (list.size() + perPage - 1) / perPage);
        page = Math.max(0, Math.min(page, pages - 1));

        int cardW = Math.max(40, (aw - (cols - 1) * GAP) / cols);
        int start = page * perPage;
        int end = Math.min(list.size(), start + perPage);
        for (int i = start; i < end; i++) {
            int idx = i - start;
            int x = ax + (idx % cols) * (cardW + GAP);
            int y = ay + (idx / cols) * (CARD_H + GAP);
            if (drawCard(ctx, list.get(i), x, y, cardW, mouseX, mouseY)) return; // un écran de réglages s'est ouvert
        }

        // ---- Pages (seulement s'il y en a plusieurs)
        if (pages > 1) {
            int fy = py + ph - 22;
            String txt = (page + 1) + " / " + pages;
            int tw = textRenderer.getWidth(txt);
            int mid = ax + aw / 2;
            ctx.drawTextWithShadow(textRenderer, txt, mid - tw / 2, fy + 4, Ui.GREY);
            boolean prev = page > 0 && Ui.button(ctx, textRenderer, "<", mid - tw / 2 - 22, fy, 16, 16, mouseX, mouseY, clicks.left, false);
            boolean next = page < pages - 1 && Ui.button(ctx, textRenderer, ">", mid + tw / 2 + 6, fy, 16, 16, mouseX, mouseY, clicks.left, false);
            if (prev) page--;
            if (next) page++;
            page = Math.max(0, Math.min(page, pages - 1));
        }
    }

    /** Dessine une carte. Renvoie true si un écran de réglages a été ouvert. */
    private boolean drawCard(DrawContext ctx, Module m, int x, int y, int w, int mouseX, int mouseY) {
        boolean on = m.get().getAsBoolean();
        boolean hover = Ui.inside(mouseX, mouseY, x, y, w, CARD_H);
        boolean hasGear = m.settings() != null;
        int gx = x + w - 13, gy = y + 5;
        boolean gearHover = hasGear && Ui.inside(mouseX, mouseY, gx - 3, gy - 3, 14, 14);

        if (hasGear && ((gearHover && clicks.left) || (hover && clicks.right))) {
            client.setScreen(m.settings().apply(this));
            return true;
        }
        if (hover && clicks.left && !gearHover) { m.set().accept(!on); on = !on; }

        Ui.rect(ctx, x, y, w, CARD_H, hover ? 0xFF17213A : 0xFF111A2E);
        if (on) ctx.fill(x + 3, y + CARD_H - 1, x + w - 3, y + CARD_H, Ui.ACCENT);

        int maxName = w - (hasGear ? 22 : 12);
        String name = m.name();
        if (textRenderer.getWidth(name) > maxName) name = textRenderer.trimToWidth(name, maxName - 6) + "..";
        ctx.drawTextWithShadow(textRenderer, name, x + 6, y + 6, Ui.WHITE);
        String desc = m.desc();
        if (textRenderer.getWidth(desc) > w - 12) desc = textRenderer.trimToWidth(desc, w - 18) + "..";
        ctx.drawText(textRenderer, desc, x + 6, y + 17, 0xFF7F8AA0, false);

        if (hasGear) ctx.drawTextWithShadow(textRenderer, "⚙", gx, gy, gearHover ? Ui.ACCENT : 0xFF7F8AA0);

        // Interrupteur
        int sx = x + 6, sy = y + CARD_H - 14, sw = 20, sh = 9;
        Ui.rect(ctx, sx, sy, sw, sh, on ? 0xFF14A880 : 0xFF2A3348);
        int kx = on ? sx + sw - 8 : sx + 1;
        ctx.fill(kx, sy + 1, kx + 7, sy + sh - 1, Ui.WHITE);
        ctx.drawText(textRenderer, on ? "Activé" : "Désactivé", sx + sw + 5, sy + 1, on ? Ui.ACCENT : 0xFF7F8AA0, false);
        return false;
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
