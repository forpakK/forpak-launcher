package fr.forpak.client.mixin;

import fr.forpak.client.ForpakClient;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.world.World;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Météo perso (visuelle, seulement de ton côté) : ciel clair, pluie ou neige. */
@Mixin(World.class)
public class WeatherWorldMixin {
    @Inject(method = "getRainGradient", at = @At("HEAD"), cancellable = true)
    private void velox$rain(float delta, CallbackInfoReturnable<Float> cir) {
        if (!((Object) this instanceof ClientWorld) || ForpakClient.CONFIG == null) return;
        int mode = ForpakClient.CONFIG.weatherMode;
        if (mode == 1) cir.setReturnValue(0f);                 // clair
        else if (mode == 2 || mode == 3) cir.setReturnValue(1f); // pluie ou neige
    }

    @Inject(method = "getThunderGradient", at = @At("HEAD"), cancellable = true)
    private void velox$thunder(float delta, CallbackInfoReturnable<Float> cir) {
        if (!((Object) this instanceof ClientWorld) || ForpakClient.CONFIG == null) return;
        if (ForpakClient.CONFIG.weatherMode != 0) cir.setReturnValue(0f);
    }
}
