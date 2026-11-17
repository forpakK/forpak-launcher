package fr.forpak.client.mixin;

import fr.forpak.client.ForpakClient;
import net.minecraft.client.particle.Particle;
import net.minecraft.client.particle.ParticleManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Module « Sans particules » : plus aucune particule (plus de FPS, écran plus clair en PvP). */
@Mixin(ParticleManager.class)
public class ParticlesMixin {
    @Inject(method = "addParticle(Lnet/minecraft/client/particle/Particle;)V", at = @At("HEAD"), cancellable = true)
    private void velox$noParticles(Particle particle, CallbackInfo ci) {
        if (ForpakClient.CONFIG != null && ForpakClient.CONFIG.noParticles) ci.cancel();
    }
}
