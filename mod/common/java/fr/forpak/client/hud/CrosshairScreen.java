package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.Crosshair;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Réglages du viseur perso (clic droit sur la carte « Viseur perso »). */
public class CrosshairScreen extends Screen {
    private static final int[] COLORS = {
            0xFF3EE6A8, 0xFF38BDF8, 0xFFFFFFFF, 0xFFFF5C5C, 0xFFFFE066, 0xFFA78BFA, 0xFFF472B6, 0xFF00FF00
    };
    private static final String[] COLOR_NAMES = {"Vert aurore", "Cyan", "Blanc", "Rouge", "Jaune", "Violet", "Rose", "Vert fluo"};

    private final Screen parent;
    private final Clicks clicks = new Clicks();

    public CrosshairScreen(Screen parent) {
        super(Text.literal("Viseur perso"));
        this.parent = parent;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);
        Config c = ForpakClient.CONFIG;

        int pw = 270, ph = 236, px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, Ui.BG_SOLID);
        Ui.outline(ctx, px, py, pw, ph, 0xFF2A6B5E);
        ctx.drawTextWithShadow(textRenderer, "Viseur perso", px + 12, py + 12, Ui.WHITE);

        // Aperçu
        int ax = px + pw - 46, ay = py + 8;
        Ui.rect(ctx, ax, ay, 36, 24, 0xFF223355);
        boolean was = c.crosshair;
        c.crosshair = true;
        ctx.enableScissor(ax, ay, ax + 36, ay + 24);
        // dessine le viseur au centre de l'aperçu
        fr.forpak.client.HudHook.push(ctx, ax + 18 - width / 2f, ay + 12 - height / 2f, 1f);
        Crosshair.render(ctx);
        fr.forpak.client.HudHook.pop(ctx);
        ctx.disableScissor();
        c.crosshair = was;

        int y = py + 40, lx = px + 12, bx = px + pw - 12 - 120;
        if (row(ctx, "Activé", c.crosshair ? "Oui" : "Non", lx, bx, y, mouseX, mouseY, c.crosshair)) c.crosshair = !c.crosshair;
        y += 26;
        if (row(ctx, "Forme", Crosshair.STYLES[c.crosshairStyle % Crosshair.STYLES.length], lx, bx, y, mouseX, mouseY, false))
            c.crosshairStyle = (c.crosshairStyle + 1) % Crosshair.STYLES.length;
        y += 26;
        c.crosshairSize = stepper(ctx, "Taille", c.crosshairSize, 1, 12, lx, bx, y, mouseX, mouseY);
        y += 26;
        c.crosshairGap = stepper(ctx, "Écart", c.crosshairGap, 0, 8, lx, bx, y, mouseX, mouseY);
        y += 26;
        int ci = 0;
        for (int i = 0; i < COLORS.length; i++) if (COLORS[i] == c.crosshairColor) ci = i;
        ctx.fill(bx - 16, y + 5, bx - 6, y + 15, c.crosshairColor);
        if (row(ctx, "Couleur", COLOR_NAMES[ci], lx, bx, y, mouseX, mouseY, false))
            c.crosshairColor = COLORS[(ci + 1) % COLORS.length];
        y += 26;
        if (row(ctx, "Contour noir", c.crosshairOutline ? "Oui" : "Non", lx, bx, y, mouseX, mouseY, c.crosshairOutline))
            c.crosshairOutline = !c.crosshairOutline;

        if (Ui.button(ctx, textRenderer, "Retour", px + 12, py + ph - 30, pw - 24, 22, mouseX, mouseY, clicks.left, true))
            close();
    }

    private boolean row(DrawContext ctx, String label, String value, int lx, int bx, int y, int mx, int my, boolean primary) {
        ctx.drawTextWithShadow(textRenderer, label, lx, y + 6, Ui.GREY);
        boolean clicked = Ui.button(ctx, textRenderer, value, bx, y, 120, 20, mx, my, clicks.left, primary);
        if (clicked) ForpakClient.CONFIG.save();
        return clicked;
    }

    private int stepper(DrawContext ctx, String label, int value, int min, int max, int lx, int bx, int y, int mx, int my) {
        ctx.drawTextWithShadow(textRenderer, label, lx, y + 6, Ui.GREY);
        if (Ui.button(ctx, textRenderer, "-", bx, y, 24, 20, mx, my, clicks.left, false)) { value = Math.max(min, value - 1); ForpakClient.CONFIG.save(); }
        String v = String.valueOf(value);
        ctx.drawTextWithShadow(textRenderer, v, bx + 60 - textRenderer.getWidth(v) / 2, y + 6, Ui.WHITE);
        if (Ui.button(ctx, textRenderer, "+", bx + 96, y, 24, 20, mx, my, clicks.left, false)) { value = Math.min(max, value + 1); ForpakClient.CONFIG.save(); }
        return value;
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
