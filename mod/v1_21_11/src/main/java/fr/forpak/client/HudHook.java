package fr.forpak.client;

import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.minecraft.util.Identifier;

/** Version 1.21.11 : nouveau système de HUD de Fabric (1.21.6+). */
public final class HudHook {
    private HudHook() { }

    public static void register() {
        HudElementRegistry.addLast(Identifier.of("forpakclient", "hud"), (ctx, tickCounter) -> Hud.render(ctx));
    }
}
