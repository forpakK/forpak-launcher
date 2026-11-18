package fr.forpak.client.mixin;

import fr.forpak.client.HudHook;
import fr.forpak.client.VeloxUsers;
import net.minecraft.client.gui.hud.PlayerListHud;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Icône Velox dans la liste des joueurs (Tab). */
@Mixin(PlayerListHud.class)
public class TabListMixin {
    @Inject(method = "getPlayerName", at = @At("RETURN"), cancellable = true)
    private void velox$icon(PlayerListEntry entry, CallbackInfoReturnable<Text> cir) {
        var id = HudHook.profileId(entry);
        Text name = cir.getReturnValue();
        if (fr.forpak.client.Shared.streamerOn() && VeloxUsers.isMe(id)) name = Text.literal("Moi");
        if (VeloxUsers.isVelox(id)) name = VeloxUsers.decorate(name);
        cir.setReturnValue(name);
    }
}
