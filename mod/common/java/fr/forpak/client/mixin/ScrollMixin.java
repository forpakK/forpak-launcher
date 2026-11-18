package fr.forpak.client.mixin;

import fr.forpak.client.Zoom;
import net.minecraft.client.Mouse;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Molette pendant le zoom : règle le zoom au lieu de changer d'objet. */
@Mixin(Mouse.class)
public class ScrollMixin {
    @Inject(method = "onMouseScroll", at = @At("HEAD"), cancellable = true)
    private void velox$zoomScroll(long window, double horizontal, double vertical, CallbackInfo ci) {
        if (Zoom.active && vertical != 0) {
            Zoom.scroll(vertical);
            ci.cancel();
        }
    }
}
