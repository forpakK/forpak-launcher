package fr.forpak.client;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;

/** Viseur personnalisé : forme, taille, écart, couleur, contour. */
public final class Crosshair {
    private Crosshair() { }

    public static final String[] STYLES = {"Croix", "Point", "Croix + point", "Cercle", "T"};

    public static boolean enabled() {
        return ForpakClient.CONFIG != null && ForpakClient.CONFIG.crosshair;
    }

    public static void render(DrawContext ctx) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.player == null || mc.options.hudHidden || !mc.options.getPerspective().isFirstPerson()) return;
        Config c = ForpakClient.CONFIG;
        int cx = ctx.getScaledWindowWidth() / 2, cy = ctx.getScaledWindowHeight() / 2;
        int len = Math.max(1, c.crosshairSize), gap = Math.max(0, c.crosshairGap), th = 1;
        int col = c.crosshairColor;
        int out = 0xC0000000;

        switch (c.crosshairStyle) {
            case 1 -> dot(ctx, cx, cy, Math.max(1, len / 3), col, out, c.crosshairOutline);
            case 3 -> circle(ctx, cx, cy, len + gap, col, out, c.crosshairOutline);
            case 4 -> {
                bar(ctx, cx - gap - len, cy, len, th, col, out, c.crosshairOutline);
                bar(ctx, cx + gap + 1, cy, len, th, col, out, c.crosshairOutline);
                bar(ctx, cx, cy + gap + 1, th, len, col, out, c.crosshairOutline);
            }
            default -> {
                bar(ctx, cx - gap - len, cy, len, th, col, out, c.crosshairOutline);
                bar(ctx, cx + gap + 1, cy, len, th, col, out, c.crosshairOutline);
                bar(ctx, cx, cy - gap - len, th, len, col, out, c.crosshairOutline);
                bar(ctx, cx, cy + gap + 1, th, len, col, out, c.crosshairOutline);
                if (c.crosshairStyle == 2) dot(ctx, cx, cy, 1, col, out, c.crosshairOutline);
            }
        }
    }

    private static void bar(DrawContext ctx, int x, int y, int w, int h, int col, int out, boolean outline) {
        if (outline) ctx.fill(x - 1, y - 1, x + w + 1, y + h + 1, out);
        ctx.fill(x, y, x + w, y + h, col);
    }

    private static void dot(DrawContext ctx, int cx, int cy, int r, int col, int out, boolean outline) {
        if (outline) ctx.fill(cx - r, cy - r, cx + r + 2, cy + r + 2, out);
        ctx.fill(cx - r + 1, cy - r + 1, cx + r + 1, cy + r + 1, col);
    }

    private static void circle(DrawContext ctx, int cx, int cy, int r, int col, int out, boolean outline) {
        for (int i = 0; i < 48; i++) {
            double a = i * Math.PI * 2 / 48;
            int x = cx + (int) Math.round(Math.cos(a) * r), y = cy + (int) Math.round(Math.sin(a) * r);
            if (outline) ctx.fill(x - 1, y - 1, x + 2, y + 2, out);
            ctx.fill(x, y, x + 1, y + 1, col);
        }
    }
}
