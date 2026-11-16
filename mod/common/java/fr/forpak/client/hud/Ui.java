package fr.forpak.client.hud;

import net.minecraft.client.gui.DrawContext;

/** Petits outils de dessin (couleurs ARGB). */
public final class Ui {
    private Ui() { }

    public static final int BG = 0x96080D19;
    public static final int BG_SOLID = 0xF40D1526;
    public static final int CARD = 0xFF141C30;
    public static final int CARD_HOVER = 0xFF1C2742;
    public static final int ACCENT = 0xFF3EE6A8;
    public static final int ACCENT_BG = 0xD014A880;
    public static final int RED = 0xFFFF5C5C;
    public static final int ORANGE = 0xFFFFC23D;
    public static final int WHITE = 0xFFFFFFFF;
    public static final int GREY = 0xFFA3AEC4;
    public static final int GREEN = 0xFF4ADE80;
    public static final int BLACK = 0xFF0B0C0F;

    /** Rectangle aux coins légèrement arrondis. */
    public static void rect(DrawContext ctx, int x, int y, int w, int h, int color) {
        if (w <= 2 || h <= 2) { ctx.fill(x, y, x + w, y + h, color); return; }
        ctx.fill(x + 1, y, x + w - 1, y + h, color);
        ctx.fill(x, y + 1, x + 1, y + h - 1, color);
        ctx.fill(x + w - 1, y + 1, x + w, y + h - 1, color);
    }

    /** Contour d'un rectangle (1 px). */
    public static void outline(DrawContext ctx, int x, int y, int w, int h, int color) {
        ctx.fill(x, y, x + w, y + 1, color);
        ctx.fill(x, y + h - 1, x + w, y + h, color);
        ctx.fill(x, y, x + 1, y + h, color);
        ctx.fill(x + w - 1, y, x + w, y + h, color);
    }

    public static boolean inside(double mx, double my, int x, int y, int w, int h) {
        return mx >= x && mx < x + w && my >= y && my < y + h;
    }
}
