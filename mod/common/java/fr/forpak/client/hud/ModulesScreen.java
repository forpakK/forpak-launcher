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

/** Liste des modules façon Lunar : clic gauche = activer, clic droit = options. */
public class ModulesScreen extends Screen {

    private record Module(String name, String desc, BooleanSupplier get, Consumer<Boolean> set,
                          java.util.function.Function<Screen, Screen> settings) { }

    // Catégories (onglets)
    private static final String[] TABS = {"HUD", "PvP", "Visuel", "Tout"};
    private static int tab = 0;
    private final java.util.Map<Module, Integer> cats = new java.util.HashMap<>();
    private int scroll = 0;

    private final Screen parent;
    private final List<Module> modules = new ArrayList<>();
    private final Clicks clicks = new Clicks();
    private int panelX, panelY, panelW, panelH;

    private static final int COLS = 4, CARD_W = 104, CARD_H = 42, GAP = 6;

    public ModulesScreen(Screen parent) {
        super(Text.literal("Modules"));
        this.parent = parent;
        Config c = ForpakClient.CONFIG;
        for (HudElement e : Hud.ELEMENTS)
            modules.add(new Module(e.name, "HUD · clic droit", e::isEnabled, e::setEnabled,
                    parentScreen -> new ModuleSettingsScreen(parentScreen, e)));
        modules.add(new Module("Viseur perso", "Clic droit : réglages", () -> c.crosshair, v -> { c.crosshair = v; c.save(); },
                CrosshairScreen::new));
        modules.add(new Module("Cape Velox", "Visible par Velox", () -> c.cape, v -> { c.cape = v; c.save(); }, null));
        modules.add(new Module("Freelook", "Maintenir Alt gauche", () -> c.freelook, v -> { c.freelook = v; c.save(); }, null));
        modules.add(new Module("Hit color", "Clic droit : couleur", () -> c.hitColorEnabled,
                v -> { c.hitColorEnabled = v; c.save(); }, HitColorScreen::new));
        modules.add(new Module("Crystal Optimizer", "Crystal PvP", () -> c.crystalOptimizer, v -> { c.crystalOptimizer = v; c.save(); }, null));
        modules.add(new Module("Anchor Optimizer", "Anchor PvP", () -> c.anchorOptimizer, v -> { c.anchorOptimizer = v; c.save(); }, null));
        modules.add(new Module("Zoom", "Touche C", () -> c.zoom, v -> { c.zoom = v; c.save(); }, null));
        modules.add(new Module("ToggleSprint", "Touche G", () -> c.toggleSprintModule, v -> { c.toggleSprintModule = v; c.save(); }, null));
        modules.add(new Module("ToggleSneak", "Touche V", () -> c.toggleSneakModule, v -> { c.toggleSneakModule = v; c.save(); }, null));
        modules.add(new Module("Menu Velox", "Écran titre perso", () -> c.customMenu, v -> { c.customMenu = v; c.save(); }, null));
    }

    private List<Module> shown() {
        if (cats.isEmpty())
            for (Module m : modules) {
                String n = m.name();
                int c = m.desc().startsWith("HUD") ? 0
                        : (n.contains("Optimizer") || n.startsWith("Toggle") || n.equals("Zoom") || n.equals("Freelook")) ? 1 : 2;
                cats.put(m, c);
            }
        if (tab == 3) return modules;
        List<Module> out = new ArrayList<>();
        for (Module m : modules) if (cats.get(m) == tab) out.add(m);
        return out;
    }

    @Override
    protected void init() {
        int rows = Math.min(5, (modules.size() + COLS - 1) / COLS);
        panelW = COLS * CARD_W + (COLS - 1) * GAP + 24;
        panelH = 34 + 24 + rows * (CARD_H + GAP) + 34;
        panelX = (width - panelW) / 2;
        panelY = Math.max(4, (height - panelH) / 2);
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0x90000000);
        clicks.poll(client);
        Ui.rect(ctx, panelX, panelY, panelW, panelH, Ui.BG_SOLID);
        Ui.outline(ctx, panelX, panelY, panelW, panelH, 0xFF2A6B5E);

        ctx.drawTextWithShadow(textRenderer, "VELOX", panelX + 12, panelY + 12, Ui.WHITE);
        ctx.drawTextWithShadow(textRenderer, ".", panelX + 12 + textRenderer.getWidth("VELOX"), panelY + 12, Ui.ACCENT);
        ctx.drawTextWithShadow(textRenderer, " MODULES", panelX + 12 + textRenderer.getWidth("VELOX."), panelY + 12, Ui.GREY);
        String hint = "Gauche : activer · Droit : options";
        ctx.drawTextWithShadow(textRenderer, hint, panelX + panelW - 12 - textRenderer.getWidth(hint), panelY + 12, Ui.GREY);

        // Onglets
        int tx = panelX + 12;
        for (int t = 0; t < TABS.length; t++) {
            int tw = textRenderer.getWidth(TABS[t]) + 18;
            boolean on = tab == t, hov = Ui.inside(mouseX, mouseY, tx, panelY + 28, tw, 18);
            Ui.rect(ctx, tx, panelY + 28, tw, 18, on ? 0xFF14A880 : hov ? Ui.CARD_HOVER : Ui.CARD);
            ctx.drawTextWithShadow(textRenderer, TABS[t], tx + 9, panelY + 33, Ui.WHITE);
            if (hov && clicks.left) { tab = t; scroll = 0; }
            tx += tw + 4;
        }

        List<Module> list = shown();
        int visibleRows = (panelH - 34 - 24 - 34 + GAP) / (CARD_H + GAP);
        int totalRows = (list.size() + COLS - 1) / COLS;
        int maxScroll = Math.max(0, totalRows - visibleRows);
        scroll = Math.max(0, Math.min(scroll, maxScroll));
        if (maxScroll > 0) {
            String pg = "▲ ▼  " + (scroll + 1) + "/" + (maxScroll + 1);
            int px2 = panelX + panelW - 12 - textRenderer.getWidth(pg);
            ctx.drawTextWithShadow(textRenderer, pg, px2, panelY + 33, Ui.GREY);
            if (clicks.left && Ui.inside(mouseX, mouseY, px2, panelY + 28, 12, 18)) scroll--;
            if (clicks.left && Ui.inside(mouseX, mouseY, px2 + 14, panelY + 28, 12, 18)) scroll++;
        }

        int startX = panelX + 12, startY = panelY + 30 + 24;
        for (int i = scroll * COLS; i < list.size() && i < (scroll + visibleRows) * COLS; i++) {
            Module m = list.get(i);
            int idx = i - scroll * COLS;
            int x = startX + (idx % COLS) * (CARD_W + GAP);
            int y = startY + (idx / COLS) * (CARD_H + GAP);
            boolean hover = Ui.inside(mouseX, mouseY, x, y, CARD_W, CARD_H);
            boolean on = m.get().getAsBoolean();
            if (hover && clicks.left) { m.set().accept(!on); on = !on; }
            if (hover && clicks.right && m.settings() != null) {
                client.setScreen(m.settings().apply(this));
                return;
            }

            Ui.rect(ctx, x, y, CARD_W, CARD_H, hover ? Ui.CARD_HOVER : Ui.CARD);
            if (hover) Ui.outline(ctx, x, y, CARD_W, CARD_H, 0x803EE6A8);
            ctx.drawTextWithShadow(textRenderer, m.name(), x + 6, y + 6, Ui.WHITE);
            ctx.drawTextWithShadow(textRenderer, m.desc(), x + 6, y + 17, Ui.GREY);

            int pw = CARD_W - 12;
            Ui.rect(ctx, x + 6, y + CARD_H - 13, pw, 9, on ? 0xFF163D2A : 0xFF4A2228);
            String s = on ? "ACTIVÉ" : "DÉSACTIVÉ";
            ctx.drawTextWithShadow(textRenderer, s, x + 6 + (pw - textRenderer.getWidth(s)) / 2, y + CARD_H - 12,
                    on ? Ui.GREEN : Ui.RED);
        }

        int by = panelY + panelH - 26;
        if (Ui.button(ctx, textRenderer, "Modifier le HUD", panelX + 12, by, 120, 20, mouseX, mouseY, clicks.left, false))
            client.setScreen(new HudEditorScreen(this));
        if (Ui.button(ctx, textRenderer, "Retour", panelX + panelW - 12 - 80, by, 80, 20, mouseX, mouseY, clicks.left, true))
            close();
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
