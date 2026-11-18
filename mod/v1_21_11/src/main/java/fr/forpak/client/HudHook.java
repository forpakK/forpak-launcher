package fr.forpak.client;

import fr.forpak.client.Crosshair;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.rendering.v1.hud.VanillaHudElements;
import net.minecraft.client.gl.RenderPipelines;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.util.Identifier;

/** Ce qui change entre les versions de Minecraft (ici : 1.21.11). */
public final class HudHook {
    private HudHook() { }

    public static void register() {
        HudElementRegistry.addLast(Identifier.of("veloxclient", "hud"),
                (ctx, tickCounter) -> fr.forpak.client.hud.Hud.render(ctx, false));
        // Viseur perso : remplace celui de Minecraft quand le module est activé
        HudElementRegistry.replaceElement(VanillaHudElements.CROSSHAIR, old -> (ctx, tickCounter) -> {
            if (Crosshair.enabled()) Crosshair.render(ctx);
            else old.render(ctx, tickCounter);
        });
    }

    /** Dessine une image (étirée sur w x h). */
    public static void blit(DrawContext ctx, Identifier tex, int x, int y, int w, int h,
                            float u, float v, int regionW, int regionH, int texW, int texH) {
        ctx.drawTexture(RenderPipelines.GUI_TEXTURED, tex, x, y, u, v, w, h, regionW, regionH, texW, texH);
    }

    /** Déplace + agrandit tout ce qui est dessiné ensuite. */
    public static void push(DrawContext ctx, float x, float y, float scale) {
        ctx.getMatrices().pushMatrix();
        ctx.getMatrices().translate(x, y);
        ctx.getMatrices().scale(scale, scale);
    }

    public static void pop(DrawContext ctx) {
        ctx.getMatrices().popMatrix();
    }

    /** UUID d'un joueur de la liste Tab. */
    public static java.util.UUID profileId(net.minecraft.client.network.PlayerListEntry e) {
        return e.getProfile().id();
    }
}
