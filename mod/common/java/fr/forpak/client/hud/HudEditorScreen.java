package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;
import org.lwjgl.glfw.GLFW;

/**
 * Éditeur de HUD : clique un élément pour le sélectionner, glisse-le pour le déplacer,
 * tire sur son coin (ou + / -) pour l'agrandir ou le rétrécir.
 */
public class HudEditorScreen extends Screen {
    private final Screen parent;
    private final Clicks clicks = new Clicks();
    private HudElement selected, dragging, resizing;
    private int offX, offY;
    private float startScale;
    private int startW, startH;
    private boolean leftWas = true;
    private int guideX = -1, guideY = -1;
    private final boolean[] keyWas = new boolean[GLFW.GLFW_KEY_LAST + 1];

    private static final int HANDLE = 7;

    public HudEditorScreen(Screen parent) {
        super(Text.literal("Éditeur de HUD"));
        this.parent = parent;
    }

    private boolean keyPressed(long h, int key) {
        boolean down = GLFW.glfwGetKey(h, key) == GLFW.GLFW_PRESS;
        boolean edge = down && !keyWas[key];
        keyWas[key] = down;
        return edge;
    }

    private void addScale(HudElement e, float delta) {
        Config.ElementStyle st = ForpakClient.CONFIG.style(e.id);
        st.scale = Math.max(0.5f, Math.min(2.5f, Math.round((st.scale + delta) * 10) / 10f));
        ForpakClient.CONFIG.save();
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        long win = client.getWindow().getHandle();
        clicks.poll(client);
        boolean down = GLFW.glfwGetMouseButton(win, GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;
        boolean pressed = down && !leftWas;

        // Fond + grille
        ctx.fill(0, 0, width, height, 0x50000000);
        for (int gx = 0; gx < width; gx += 20) ctx.fill(gx, 0, gx + 1, height, 0x0FFFFFFF);
        for (int gy = 0; gy < height; gy += 20) ctx.fill(0, gy, width, gy + 1, 0x0FFFFFFF);
        ctx.fill(width / 2, 0, width / 2 + 1, height, 0x183EE6A8);
        ctx.fill(0, height / 2, width, height / 2 + 1, 0x183EE6A8);

        Hud.render(ctx, true);

        // ---- Barre d'outils de l'élément sélectionné (taille, réglages)
        boolean toolbarClicked = false;
        if (selected != null && selected.isEnabled() && dragging == null && resizing == null) {
            Config.ElementStyle st = ForpakClient.CONFIG.style(selected.id);
            int tw = 176, th = 20;
            int tx = Math.max(2, Math.min(width - tw - 2, selected.lastX + selected.lastW / 2 - tw / 2));
            int ty = selected.lastY - th - 6;
            if (ty < 2) ty = selected.lastY + selected.lastH + 6;
            Ui.rect(ctx, tx, ty, tw, th, Ui.BG_SOLID);
            Ui.outline(ctx, tx, ty, tw, th, 0xFF2A6B5E);
            String label = selected.name;
            if (textRenderer.getWidth(label) > 70) label = textRenderer.trimToWidth(label, 66) + "…";
            ctx.drawTextWithShadow(textRenderer, label, tx + 6, ty + 6, Ui.WHITE);
            int bx = tx + tw - 100;
            if (Ui.button(ctx, textRenderer, "-", bx, ty + 2, 16, 16, mouseX, mouseY, clicks.left, false)) { addScale(selected, -0.1f); toolbarClicked = true; }
            String pct = Math.round(st.scale * 100) + "%";
            ctx.drawTextWithShadow(textRenderer, pct, bx + 34 - textRenderer.getWidth(pct) / 2, ty + 6, Ui.ACCENT);
            if (Ui.button(ctx, textRenderer, "+", bx + 52, ty + 2, 16, 16, mouseX, mouseY, clicks.left, false)) { addScale(selected, 0.1f); toolbarClicked = true; }
            if (Ui.button(ctx, textRenderer, "Style", bx + 72, ty + 2, 26, 16, mouseX, mouseY, clicks.left, false)) {
                client.setScreen(new ModuleSettingsScreen(this, selected));
                return;
            }
            if (Ui.inside(mouseX, mouseY, tx, ty, tw, th)) toolbarClicked |= pressed;
        }

        // ---- Début d'un glisser : poignée de taille, sinon élément
        if (pressed && !toolbarClicked && dragging == null && resizing == null) {
            if (selected != null && selected.isEnabled() && onHandle(selected, mouseX, mouseY)) {
                resizing = selected;
                startScale = ForpakClient.CONFIG.style(selected.id).scale;
                startW = Math.max(1, selected.lastW);
                startH = Math.max(1, selected.lastH);
            } else {
                HudElement hit = null;
                for (int i = Hud.ELEMENTS.size() - 1; i >= 0; i--) {
                    HudElement e = Hud.ELEMENTS.get(i);
                    if (e.isEnabled() && Ui.inside(mouseX, mouseY, e.lastX, e.lastY, e.lastW, e.lastH)) { hit = e; break; }
                }
                selected = hit;
                if (hit != null) {
                    dragging = hit;
                    offX = mouseX - hit.lastX;
                    offY = mouseY - hit.lastY;
                }
            }
        }

        // ---- Redimensionnement (poignée du coin)
        if (resizing != null) {
            if (down) {
                float rx = (mouseX - resizing.lastX) / (float) startW;
                float ry = (mouseY - resizing.lastY) / (float) startH;
                float ratio = Math.max(0.1f, (rx + ry) / 2f);
                Config.ElementStyle st = ForpakClient.CONFIG.style(resizing.id);
                st.scale = Math.max(0.5f, Math.min(2.5f, Math.round(startScale * ratio * 20) / 20f));
            } else {
                resizing = null;
                ForpakClient.CONFIG.save();
            }
        }

        // ---- Déplacement (avec aimantage, Shift = libre)
        if (dragging != null) {
            if (down) {
                int nx = mouseX - offX, ny = mouseY - offY;
                boolean free = GLFW.glfwGetKey(win, GLFW.GLFW_KEY_LEFT_SHIFT) == GLFW.GLFW_PRESS;
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
        leftWas = down;

        // ---- Clavier : + / - sur l'élément sélectionné (ou survolé)
        HudElement target = selected;
        if (target == null)
            for (HudElement e : Hud.ELEMENTS)
                if (e.isEnabled() && Ui.inside(mouseX, mouseY, e.lastX, e.lastY, e.lastW, e.lastH)) target = e;
        boolean plus = keyPressed(win, GLFW.GLFW_KEY_KP_ADD) | keyPressed(win, GLFW.GLFW_KEY_EQUAL);
        boolean minus = keyPressed(win, GLFW.GLFW_KEY_KP_SUBTRACT) | keyPressed(win, GLFW.GLFW_KEY_MINUS) | keyPressed(win, GLFW.GLFW_KEY_6);
        if (target != null && plus) addScale(target, 0.1f);
        if (target != null && minus) addScale(target, -0.1f);

        // ---- Guides
        if (guideX >= 0) ctx.fill(guideX, 0, guideX + 1, height, 0xC03EE6A8);
        if (guideY >= 0) ctx.fill(0, guideY, width, guideY + 1, 0xC03EE6A8);

        // ---- Cadres + poignée
        for (HudElement e : Hud.ELEMENTS) {
            if (!e.isEnabled()) continue;
            boolean hover = Ui.inside(mouseX, mouseY, e.lastX, e.lastY, e.lastW, e.lastH);
            boolean sel = e == selected;
            Ui.outline(ctx, e.lastX - 1, e.lastY - 1, e.lastW + 2, e.lastH + 2,
                    sel ? Ui.ACCENT : hover ? 0xC0FFFFFF : 0x60FFFFFF);
            if (sel) {
                int hx = e.lastX + e.lastW - HANDLE / 2, hy = e.lastY + e.lastH - HANDLE / 2;
                ctx.fill(hx - 1, hy - 1, hx + HANDLE + 1, hy + HANDLE + 1, 0xFF0B0C0F);
                ctx.fill(hx, hy, hx + HANDLE, hy + HANDLE, onHandle(e, mouseX, mouseY) || resizing == e ? Ui.WHITE : Ui.ACCENT);
            }
        }

        // ---- Aide + boutons
        String hint = "Clique pour choisir · Glisse pour déplacer · Coin ou + / - pour la taille · Shift = sans aimant";
        int hw = textRenderer.getWidth(hint) + 16;
        int hy = height - 52;
        Ui.rect(ctx, width / 2 - hw / 2, hy, hw, 16, 0xC00D1526);
        ctx.drawTextWithShadow(textRenderer, hint, width / 2 - hw / 2 + 8, hy + 4, Ui.GREY);
        if (Ui.button(ctx, textRenderer, "Terminé", width / 2 - 104, height - 30, 100, 20, mouseX, mouseY, clicks.left, true)) { close(); return; }
        if (Ui.button(ctx, textRenderer, "Tout réinitialiser", width / 2 + 4, height - 30, 100, 20, mouseX, mouseY, clicks.left, false)) {
            ForpakClient.CONFIG.pos.clear();
            for (Config.ElementStyle st : ForpakClient.CONFIG.styles.values()) st.scale = 1f;
            ForpakClient.CONFIG.save();
        }
    }

    private boolean onHandle(HudElement e, int mx, int my) {
        int hx = e.lastX + e.lastW - HANDLE / 2, hy = e.lastY + e.lastH - HANDLE / 2;
        return mx >= hx - 2 && mx <= hx + HANDLE + 2 && my >= hy - 2 && my <= hy + HANDLE + 2;
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        ForpakClient.CONFIG.save();
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() { return false; }
}
