package fr.forpak.client.mixin;

import fr.forpak.client.Capes;
import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.entity.player.SkinTextures;
import net.minecraft.util.AssetInfo;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Cape Velox (1.21.11). */
@Mixin(AbstractClientPlayerEntity.class)
public class CapeMixin {
    @Inject(method = "getSkin", at = @At("RETURN"), cancellable = true)
    private void velox$cape(CallbackInfoReturnable<SkinTextures> cir) {
        Identifier cape = Capes.capeFor(((AbstractClientPlayerEntity) (Object) this).getUuid());
        if (cape == null) return;
        SkinTextures t = cir.getReturnValue();
        AssetInfo.TextureAsset asset = new AssetInfo.TextureAssetInfo(Identifier.of("veloxclient", "cape/velox"), cape);
        cir.setReturnValue(new SkinTextures(t.body(), asset, t.elytra(), t.model(), t.secure()));
    }
}
