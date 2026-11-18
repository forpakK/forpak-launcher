package fr.forpak.client;

import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.util.Identifier;

/** Ce qui change entre les versions de Minecraft (ici : 1.21.1). */
public final class HudHook {
    private HudHook() { }

    public static void register() {
        HudRenderCallback.EVENT.register((ctx, tickCounter) -> fr.forpak.client.hud.Hud.render(ctx, false));
    }

    /** Dessine une image (étirée sur w x h). */
    public static void blit(DrawContext ctx, Identifier tex, int x, int y, int w, int h,
                            float u, float v, int regionW, int regionH, int texW, int texH) {
        ctx.drawTexture(tex, x, y, w, h, u, v, regionW, regionH, texW, texH);
    }

    /** Déplace + agrandit tout ce qui est dessiné ensuite. */
    public static void push(DrawContext ctx, float x, float y, float scale) {
        ctx.getMatrices().push();
        ctx.getMatrices().translate(x, y, 0);
        ctx.getMatrices().scale(scale, scale, 1);
    }

    public static void pop(DrawContext ctx) {
        ctx.getMatrices().pop();
    }

    /** UUID d'un joueur de la liste Tab. */
    public static java.util.UUID profileId(net.minecraft.client.network.PlayerListEntry e) {
        return e.getProfile().getId();
    }
}
