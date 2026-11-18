package fr.forpak.client.mixin;

import fr.forpak.client.Stats;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.network.ClientPlayNetworkHandler;
import net.minecraft.entity.Entity;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.network.packet.s2c.play.EntityStatusS2CPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Détecte quand un joueur utilise un totem (statut 35) ou meurt (statut 3). */
@Mixin(ClientPlayNetworkHandler.class)
public class EntityStatusMixin {

    @Inject(method = "onEntityStatus", at = @At("TAIL"))
    private void forpak$onStatus(EntityStatusS2CPacket packet, CallbackInfo ci) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.world == null) return;
        Entity e = packet.getEntity(mc.world);
        if (!(e instanceof PlayerEntity)) return;
        String name = e.getName().getString();
        if (packet.getStatus() == 35) Stats.onTotemPop(name);
        else if (packet.getStatus() == 3) Stats.onDeath(e);
    }
}
