package fr.forpak.client.mixin;

import fr.forpak.client.Freelook;
import net.minecraft.client.render.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArgs;
import org.spongepowered.asm.mixin.injection.invoke.arg.Args;

/** Freelook : la caméra utilise notre angle de vue. */
@Mixin(Camera.class)
public class FreelookCameraMixin {
    @ModifyArgs(method = "update", at = @At(value = "INVOKE",
            target = "Lnet/minecraft/client/render/Camera;setRotation(FF)V"))
    private void velox$freelookRotation(Args args) {
        if (!Freelook.active) return;
        args.set(0, Freelook.yaw);
        args.set(1, Freelook.pitch);
    }
}
