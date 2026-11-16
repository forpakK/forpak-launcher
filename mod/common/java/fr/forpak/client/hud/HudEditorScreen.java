package fr.forpak.client.hud;

import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;
import org.lwjgl.glfw.GLFW;

/** Éditeur de HUD : glisse les éléments où tu veux. */
public class HudEditorScreen extends Screen {
    private final Screen parent;
    private HudElement dragging;
    private int offX, offY;
    private boolean mouseWasDown = true;
    private int guideX = -1, guideY = -1;

    public HudEditorScreen(Screen parent) {
        super(Text.literal("Éditeur de HUD"));
        this.parent = parent;
    }

    @Override
    protected void init() {
        addDrawableChild(ButtonWidget.builder(Text.literal("Terminé"), b -> close())
                .dimensions(width / 2 - 75, height / 2 - 10, 70, 20).build());
        addDrawableChild(ButtonWidget.builder(Text.literal("Réinitialiser"), b -> {
            ForpakClient.CONFIG.pos.clear();
            ForpakClient.CONFIG.save();
        }).dimensions(width / 2 + 5, height / 2 - 10, 80, 20).build());
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        ctx.fill(0, 0, width, height, 0x50000000);
        // Grille légère
        for (int gx = 0; gx < width; gx += 20) ctx.fill(gx, 0, gx + 1, height, 0x10FFFFFF);
        for (int gy = 0; gy < height; gy += 20) ctx.fill(0, gy, width, gy + 1, 0x10FFFFFF);

        Hud.render(ctx, true);

        boolean down = client != null && GLFW.glfwGetMouseButton(client.getWindow().getHandle(),
                GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;

        if (down && !mouseWasDown && dragging == null) {
            for (int i = Hud.ELEMENTS.size() - 1; i >= 0; i--) {
                HudElement e = Hud.ELEMENTS.get(i);
                if (e.isEnabled() && Ui.inside(mouseX, mouseY, e.lastX, e.lastY, e.lastW, e.lastH)) {
                    dragging = e;
                    offX = mouseX - e.lastX;
                    offY = mouseY - e.lastY;
                    break;
                }
            }
        }
        if (dragging != null) {
            if (down) {
                int nx = mouseX - offX, ny = mouseY - offY;
                // Aimantage : bords de l'écran et centre (désactivé avec Shift)
                boolean free = client != null && org.lwjgl.glfw.GLFW.glfwGetKey(client.getWindow().getHandle(), org.lwjgl.glfw.GLFW.GLFW_KEY_LEFT_SHIFT) == org.lwjgl.glfw.GLFW.GLFW_PRESS;
                int snap = 6;
                guideX = -1; guideY = -1;
                if (!free) {
                    int cxe = nx + dragging.lastW / 2, cye = ny + dragging.lastH / 2;
                    if (Math.abs(nx) < snap) nx = 0;
                    if (Math.abs(nx + dragging.lastW - width) < snap) nx = width - dragging.lastW;
                    if (Math.abs(cxe - width / 2) < snap) { nx = width / 2 - dragging.lastW / 2; guideX = width / 2; }
                    if (Math.abs(ny) < snap) ny = 0;
                    if (Math.abs(ny + dragging.lastH - height) < snap) ny = height - dragging.lastH;
                    if (Math.abs(cye - height / 2) < snap) { ny = height / 2 - dragging.lastH / 2; guideY = height / 2; }
                }
                float fx = width - dragging.lastW <= 0 ? 0 : (float) nx / (width - dragging.lastW);
                float fy = height - dragging.lastH <= 0 ? 0 : (float) ny / (height - dragging.lastH);
                dragging.setPosition(fx, fy);
            } else {
                dragging = null;
                guideX = guideY = -1;
                ForpakClient.CONFIG.save();
            }
        }
        mouseWasDown = down;

        // Guides d'alignement
        if (guideX >= 0) ctx.fill(guideX, 0, guideX + 1, height, 0xC03EE6A8);
        if (guideY >= 0) ctx.fill(0, guideY, width, guideY + 1, 0xC03EE6A8);

        // Cadres autour des éléments
        for (HudElement e : Hud.ELEMENTS) {
            if (!e.isEnabled()) continue;
            boolean hover = Ui.inside(mouseX, mouseY, e.lastX, e.lastY, e.lastW, e.lastH);
            Ui.outline(ctx, e.lastX - 1, e.lastY - 1, e.lastW + 2, e.lastH + 2,
                    e == dragging || hover ? Ui.ACCENT : 0x80FFFFFF);
        }

        String hint = "Glisse les éléments · Shift = sans aimant · Échap pour terminer";
        ctx.drawTextWithShadow(textRenderer, hint, (width - textRenderer.getWidth(hint)) / 2, height / 2 - 24, Ui.WHITE);
        super.render(ctx, mouseX, mouseY, delta);
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        ForpakClient.CONFIG.save();
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
