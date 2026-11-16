package fr.forpak.client.mixin;

import fr.forpak.client.Crosshair;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.InGameHud;
import net.minecraft.client.render.RenderTickCounter;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Viseur perso (1.21.1) : remplace le viseur de Minecraft quand le module est activé. */
@Mixin(InGameHud.class)
public class CrosshairMixin {
    @Inject(method = "renderCrosshair", at = @At("HEAD"), cancellable = true)
    private void velox$crosshair(DrawContext context, RenderTickCounter tickCounter, CallbackInfo ci) {
        if (!Crosshair.enabled()) return;
        Crosshair.render(context);
        ci.cancel();
    }
}
