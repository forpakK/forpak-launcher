package fr.forpak.client;

import fr.forpak.client.hud.Ui;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;

import java.util.ArrayList;
import java.util.List;

/** Petites notifications Velox en haut à droite (amis connectés, invitations...). */
public final class Toasts {
    private Toasts() { }

    private record Toast(String title, String text, int color, long at, long life) { }
    private static final List<Toast> LIST = new ArrayList<>();

    public static synchronized void show(String title, String text, int color, long lifeMs) {
        LIST.add(new Toast(title, text, color, System.currentTimeMillis(), lifeMs));
        while (LIST.size() > 4) LIST.remove(0);
    }

    public static synchronized void render(DrawContext ctx, MinecraftClient mc, int sw) {
        long now = System.currentTimeMillis();
        LIST.removeIf(t -> now - t.at() > t.life());
        int y = 6;
        for (Toast t : LIST) {
            var tr = mc.textRenderer;
            int w = Math.max(tr.getWidth(t.title()), tr.getWidth(t.text())) + 18;
            long age = now - t.at();
            // Glisse depuis la droite à l'arrivée et au départ
            float slide = Math.min(1f, age / 180f) * Math.min(1f, (t.life() - age) / 180f);
            int x = sw - 6 - (int) (w * slide);
            Ui.rect(ctx, x, y, w, 26, 0xF00D1526);
            ctx.fill(x, y + 3, x + 2, y + 23, t.color());
            ctx.drawTextWithShadow(tr, t.title(), x + 9, y + 4, t.color());
            ctx.drawTextWithShadow(tr, t.text(), x + 9, y + 15, Ui.WHITE);
            // Barre de temps restant
            int bw = (int) ((w - 4) * Math.max(0, 1 - age / (double) t.life()));
            ctx.fill(x + 2, y + 25, x + 2 + bw, y + 26, (t.color() & 0x00FFFFFF) | 0x80000000);
            y += 30;
        }
    }
}
