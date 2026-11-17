package fr.forpak.client.mixin;

import fr.forpak.client.ForpakClient;
import net.minecraft.world.biome.Biome;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Météo perso : force pluie ou neige partout (même dans le désert). */
@Mixin(Biome.class)
public class WeatherBiomeMixin {
    @Inject(method = "getPrecipitation", at = @At("HEAD"), cancellable = true)
    private void velox$precipitation(CallbackInfoReturnable<Biome.Precipitation> cir) {
        if (ForpakClient.CONFIG == null || net.minecraft.client.MinecraftClient.getInstance().world == null) return;
        int mode = ForpakClient.CONFIG.weatherMode;
        if (mode == 2) cir.setReturnValue(Biome.Precipitation.RAIN);
        else if (mode == 3) cir.setReturnValue(Biome.Precipitation.SNOW);
    }
}
