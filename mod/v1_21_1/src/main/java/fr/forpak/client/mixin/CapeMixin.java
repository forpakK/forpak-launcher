package fr.forpak.client.mixin;

import fr.forpak.client.Capes;
import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.client.util.SkinTextures;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Cape Velox (1.21.1). */
@Mixin(AbstractClientPlayerEntity.class)
public class CapeMixin {
    @Inject(method = "getSkinTextures", at = @At("RETURN"), cancellable = true)
    private void velox$cape(CallbackInfoReturnable<SkinTextures> cir) {
        Identifier cape = Capes.capeFor(((AbstractClientPlayerEntity) (Object) this).getUuid());
        if (cape == null) return;
        SkinTextures t = cir.getReturnValue();
        cir.setReturnValue(new SkinTextures(t.texture(), t.textureUrl(), cape, t.elytraTexture(), t.model(), t.secure()));
    }
}
