package fr.forpak.client.mixin;

import fr.forpak.client.Freelook;
import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Freelook : la souris tourne la caméra au lieu du joueur. */
@Mixin(Entity.class)
public class FreelookEntityMixin {
    @Inject(method = "changeLookDirection", at = @At("HEAD"), cancellable = true)
    private void velox$freelook(double cursorDeltaX, double cursorDeltaY, CallbackInfo ci) {
        if (Freelook.active && (Object) this == MinecraftClient.getInstance().player) {
            Freelook.turn(cursorDeltaX, cursorDeltaY);
            ci.cancel();
        }
    }
}
