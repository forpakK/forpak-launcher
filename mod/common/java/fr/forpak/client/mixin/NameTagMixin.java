package fr.forpak.client.mixin;

import fr.forpak.client.VeloxUsers;
import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Icône Velox devant le pseudo au-dessus de la tête. */
@Mixin(PlayerEntity.class)
public class NameTagMixin {
    @Inject(method = "getDisplayName", at = @At("RETURN"), cancellable = true)
    private void velox$icon(CallbackInfoReturnable<Text> cir) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc == null || !mc.isOnThread()) return;          // jamais côté serveur (solo)
        PlayerEntity self = (PlayerEntity) (Object) this;
        Text name = cir.getReturnValue();
        if (fr.forpak.client.Shared.streamerOn() && VeloxUsers.isMe(self.getUuid())) name = Text.literal("Moi");
        if (VeloxUsers.isVelox(self.getUuid())) name = VeloxUsers.decorate(name);
        cir.setReturnValue(name);
    }
}
