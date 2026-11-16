package fr.forpak.client;

import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;

/** Version 1.21.1 : ancien système de HUD de Fabric. */
public final class HudHook {
    private HudHook() { }

    public static void register() {
        HudRenderCallback.EVENT.register((ctx, tickCounter) -> Hud.render(ctx));
    }
}
