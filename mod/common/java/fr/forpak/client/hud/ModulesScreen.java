package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;
import org.lwjgl.glfw.GLFW;

import java.util.ArrayList;
import java.util.List;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;

/** Menu des modules façon Lunar (Shift droit). */
public class ModulesScreen extends Screen {

    private record Module(String name, String desc, BooleanSupplier get, Consumer<Boolean> set) { }

    private final List<Module> modules = new ArrayList<>();
    private boolean mouseWasDown = true; // évite de cliquer en ouvrant le menu
    private int panelX, panelY, panelW, panelH;

    private static final int COLS = 4, CARD_W = 104, CARD_H = 42, GAP = 6;

    public ModulesScreen() {
        super(Text.literal("Velox Client"));
        Config c = ForpakClient.CONFIG;
        for (HudElement e : Hud.ELEMENTS)
            modules.add(new Module(e.name, "HUD", e::isEnabled, e::setEnabled));
        modules.add(new Module("Crystal Optimizer", "PvP", () -> c.crystalOptimizer, v -> { c.crystalOptimizer = v; c.save(); }));
        modules.add(new Module("Zoom", "Touche C", () -> c.zoom, v -> { c.zoom = v; c.save(); }));
        modules.add(new Module("ToggleSprint", "Touche G", () -> c.toggleSprintModule, v -> { c.toggleSprintModule = v; c.save(); }));
        modules.add(new Module("ToggleSneak", "Touche V", () -> c.toggleSneakModule, v -> { c.toggleSneakModule = v; c.save(); }));
    }

    @Override
    protected void init() {
        int rows = (modules.size() + COLS - 1) / COLS;
        panelW = COLS * CARD_W + (COLS - 1) * GAP + 24;
        panelH = 34 + rows * (CARD_H + GAP) + 34;
        panelX = (width - panelW) / 2;
        panelY = Math.max(4, (height - panelH) / 2);

        int by = panelY + panelH - 26;
        addDrawableChild(ButtonWidget.builder(Text.literal("Modifier le HUD"), b -> {
            if (client != null) client.setScreen(new HudEditorScreen(this));
        }).dimensions(panelX + 12, by, 120, 20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("Fermer"), b -> close())
                .dimensions(panelX + panelW - 12 - 80, by, 80, 20).build());
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        ctx.fill(0, 0, width, height, 0x90000000);
        Ui.rect(ctx, panelX, panelY, panelW, panelH, Ui.BG_SOLID);
        Ui.outline(ctx, panelX, panelY, panelW, panelH, 0xFF24406F);

        // Titre
        ctx.drawTextWithShadow(textRenderer, "VELOX", panelX + 12, panelY + 12, Ui.WHITE);
        ctx.drawTextWithShadow(textRenderer, ".", panelX + 12 + textRenderer.getWidth("VELOX"), panelY + 12, Ui.ACCENT);
        ctx.drawTextWithShadow(textRenderer, " CLIENT", panelX + 12 + textRenderer.getWidth("VELOX."), panelY + 12, Ui.GREY);
        String hint = "Clique pour activer · F8 cache le HUD";
        ctx.drawTextWithShadow(textRenderer, hint, panelX + panelW - 12 - textRenderer.getWidth(hint), panelY + 12, Ui.GREY);

        // Clic (on lit la souris directement : compatible 1.21.1 et 1.21.11)
        boolean down = client != null && GLFW.glfwGetMouseButton(client.getWindow().getHandle(),
                GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;
        boolean clicked = down && !mouseWasDown;
        mouseWasDown = down;

        int startX = panelX + 12, startY = panelY + 30;
        for (int i = 0; i < modules.size(); i++) {
            Module m = modules.get(i);
            int x = startX + (i % COLS) * (CARD_W + GAP);
            int y = startY + (i / COLS) * (CARD_H + GAP);
            boolean hover = Ui.inside(mouseX, mouseY, x, y, CARD_W, CARD_H);
            boolean on = m.get().getAsBoolean();
            if (hover && clicked) { m.set().accept(!on); on = !on; }

            Ui.rect(ctx, x, y, CARD_W, CARD_H, hover ? Ui.CARD_HOVER : Ui.CARD);
            ctx.drawTextWithShadow(textRenderer, m.name(), x + 6, y + 6, Ui.WHITE);
            ctx.drawTextWithShadow(textRenderer, m.desc(), x + 6, y + 17, Ui.GREY);

            // Pastille ON/OFF
            int pw = CARD_W - 12;
            Ui.rect(ctx, x + 6, y + CARD_H - 13, pw, 9, on ? 0xFF163D2A : 0xFF4A2228);
            String s = on ? "ACTIVÉ" : "DÉSACTIVÉ";
            ctx.drawTextWithShadow(textRenderer, s, x + 6 + (pw - textRenderer.getWidth(s)) / 2, y + CARD_H - 12,
                    on ? Ui.GREEN : Ui.RED);
        }

        super.render(ctx, mouseX, mouseY, delta);
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) {
        // fond dessiné nous-mêmes dans render()
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
