package fr.forpak.client;

import net.minecraft.entity.Entity;
import net.minecraft.entity.decoration.EndCrystalEntity;
import net.minecraft.entity.effect.StatusEffects;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.util.ActionResult;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.EntityHitResult;
import net.minecraft.world.World;

/**
 * Crystal optimizer : quand tu tapes un crystal, il disparaît tout de suite de ton écran
 * au lieu d'attendre la réponse du serveur. Tu peux replacer le suivant plus vite.
 * (Le serveur reçoit quand même l'attaque normalement.)
 */
public final class CrystalOptimizer {
    private CrystalOptimizer() { }

    public static ActionResult onAttack(PlayerEntity player, World world, Hand hand, Entity entity, EntityHitResult hit) {
        if (!world.isClient() || !ForpakClient.CONFIG.crystalOptimizer) return ActionResult.PASS;
        if (!(entity instanceof EndCrystalEntity) || player.isSpectator()) return ActionResult.PASS;

        // Avec Faiblesse (sans Force), le coup ne casse pas forcément le crystal : on ne fait rien
        if (player.hasStatusEffect(StatusEffects.WEAKNESS) && !player.hasStatusEffect(StatusEffects.STRENGTH))
            return ActionResult.PASS;

        entity.discard();
        return ActionResult.PASS; // le paquet d'attaque part quand même au serveur
    }
}
