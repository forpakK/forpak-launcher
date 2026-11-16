package fr.forpak.client.hud;

import fr.forpak.client.ForpakClient;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;

/** Un élément du HUD qu'on peut activer et déplacer. */
public abstract class HudElement {
    public final String id;
    public final String name;
    public final boolean defaultOn;
    private final float defX, defY;

    // Dernière position dessinée (pour l'éditeur)
    public int lastX, lastY, lastW, lastH;

    protected HudElement(String id, String name, boolean defaultOn, float defX, float defY) {
        this.id = id;
        this.name = name;
        this.defaultOn = defaultOn;
        this.defX = defX;
        this.defY = defY;
    }

    public boolean isEnabled() { return ForpakClient.CONFIG.isOn(id, defaultOn); }

    public void setEnabled(boolean on) {
        ForpakClient.CONFIG.enabled.put(id, on);
        ForpakClient.CONFIG.save();
    }

    public float[] position() {
        float[] p = ForpakClient.CONFIG.pos.get(id);
        return p != null && p.length == 2 ? p : new float[]{defX, defY};
    }

    public void setPosition(float x, float y) {
        ForpakClient.CONFIG.pos.put(id, new float[]{clamp01(x), clamp01(y)});
    }

    /** Faut-il l'afficher en jeu ? (ex : Reach seulement après un coup) */
    public boolean visible(MinecraftClient mc) { return true; }

    public abstract int width(MinecraftClient mc, boolean editing);
    public abstract int height(MinecraftClient mc, boolean editing);
    public abstract void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing);

    private static float clamp01(float v) { return Math.max(0f, Math.min(1f, v)); }
}
