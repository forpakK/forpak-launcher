package fr.forpak.client.hud;

import fr.forpak.client.HudHook;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.util.Identifier;

/** Logo Velox + nom, et le décor d'hiver (ciel, aurores, neige). */
public final class Logo {
    private Logo() { }

    public static final Identifier LOGO = Identifier.of("veloxclient", "textures/gui/logo.png");
    public static final Identifier SKY = Identifier.of("veloxclient", "textures/gui/sky.png");
    public static final Identifier AURORA = Identifier.of("veloxclient", "textures/gui/aurora.png");
    public static final Identifier LAND = Identifier.of("veloxclient", "textures/gui/land.png");

    /** Logo centré en (cx, y), taille size, avec "VELOX. CLIENT" dessous. Renvoie le bas du bloc. */
    public static int draw(DrawContext ctx, TextRenderer tr, int cx, int y, int size) {
        HudHook.blit(ctx, LOGO, cx - size / 2, y, size, size, 0, 0, 256, 256, 256, 256);
        int ty = y + size + 6;
        String velox = "VELOX";
        int w = tr.getWidth(velox + ".") * 2;
        HudHook.push(ctx, cx - w / 2f, ty, 2f);
        ctx.drawTextWithShadow(tr, velox, 0, 0, Ui.WHITE);
        ctx.drawTextWithShadow(tr, ".", tr.getWidth(velox), 0, Ui.ACCENT);
        HudHook.pop(ctx);
        String client = "C L I E N T";
        ctx.drawTextWithShadow(tr, client, cx - tr.getWidth(client) / 2, ty + 20, 0xFF8CF5CF);
        return ty + 32;
    }

    // ---- décor animé
    private static final float[][] SNOW = new float[90][4];
    static {
        java.util.Random r = new java.util.Random(7);
        for (float[] f : SNOW) { f[0] = r.nextFloat(); f[1] = r.nextFloat(); f[2] = 1 + r.nextInt(3); f[3] = r.nextFloat() * 6.28f; }
    }

    public static void background(DrawContext ctx, int width, int height) {
        // Ciel qui remplit l'écran (en gardant les proportions)
        float scale = Math.max(width / 960f, height / 540f);
        int bw = Math.round(960 * scale), bh = Math.round(540 * scale);
        int bx = (width - bw) / 2, by = (height - bh) / 2;
        HudHook.blit(ctx, SKY, bx, by, bw, bh, 0, 0, 960, 540, 960, 540);

        // Aurores qui défilent doucement
        long t = System.currentTimeMillis();
        int aw = bw, ah = bh / 2;
        int off = (int) ((t / 60) % aw);
        HudHook.blit(ctx, AURORA, bx - off, by, aw, ah, 0, 0, 960, 270, 960, 270);
        HudHook.blit(ctx, AURORA, bx - off + aw, by, aw, ah, 0, 0, 960, 270, 960, 270);

        // Collines enneigées
        HudHook.blit(ctx, LAND, bx, by, bw, bh, 0, 0, 960, 540, 960, 540);

        // Neige
        float sec = (t % 1000000L) / 1000f;
        for (float[] f : SNOW) {
            float speed = 0.02f + f[2] * 0.015f;
            float y = (f[1] + sec * speed) % 1f;
            float x = f[0] + (float) Math.sin(sec * 0.8f + f[3]) * 0.01f;
            int px = Math.round(x * width), py = Math.round(y * height);
            int s = (int) f[2];
            ctx.fill(px, py, px + s, py + s, s == 1 ? 0x99EBF3FF : 0xDDEBF3FF);
        }
    }
}
