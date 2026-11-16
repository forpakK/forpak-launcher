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

    private record Module(String name, String desc, BooleanSupplier get, Consumer<Boolean> set, HudElement element) { }

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
            modules.add(new Module(e.name, "HUD · clic droit", e::isEnabled, e::setEnabled, e));
        modules.add(new Module("Crystal Optimizer", "Crystal PvP", () -> c.crystalOptimizer, v -> { c.crystalOptimizer = v; c.save(); }, null));
        modules.add(new Module("Anchor Optimizer", "Anchor PvP", () -> c.anchorOptimizer, v -> { c.anchorOptimizer = v; c.save(); }, null));
        modules.add(new Module("Zoom", "Touche C", () -> c.zoom, v -> { c.zoom = v; c.save(); }, null));
        modules.add(new Module("ToggleSprint", "Touche G", () -> c.toggleSprintModule, v -> { c.toggleSprintModule = v; c.save(); }, null));
        modules.add(new Module("ToggleSneak", "Touche V", () -> c.toggleSneakModule, v -> { c.toggleSneakModule = v; c.save(); }, null));
        modules.add(new Module("Menu Velox", "Écran titre perso", () -> c.customMenu, v -> { c.customMenu = v; c.save(); }, null));
    }

    @Override
    protected void init() {
        int rows = (modules.size() + COLS - 1) / COLS;
        panelW = COLS * CARD_W + (COLS - 1) * GAP + 24;
        panelH = 34 + rows * (CARD_H + GAP) + 34;
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

        int startX = panelX + 12, startY = panelY + 30;
        for (int i = 0; i < modules.size(); i++) {
            Module m = modules.get(i);
            int x = startX + (i % COLS) * (CARD_W + GAP);
            int y = startY + (i / COLS) * (CARD_H + GAP);
            boolean hover = Ui.inside(mouseX, mouseY, x, y, CARD_W, CARD_H);
            boolean on = m.get().getAsBoolean();
            if (hover && clicks.left) { m.set().accept(!on); on = !on; }
            if (hover && clicks.right && m.element() != null) {
                client.setScreen(new ModuleSettingsScreen(this, m.element()));
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
