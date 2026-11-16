package fr.forpak.client;

import net.minecraft.block.BlockState;
import net.minecraft.block.Blocks;
import net.minecraft.block.RespawnAnchorBlock;
import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.Items;
import net.minecraft.util.ActionResult;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;

/**
 * Anchor optimizer : quand tu fais exploser une ancre de réapparition (hors Nether),
 * elle disparaît tout de suite de ton écran au lieu d'attendre le serveur.
 * Tu peux reposer la suivante plus vite. Le serveur reçoit le clic normalement.
 */
public final class AnchorOptimizer {
    private AnchorOptimizer() { }

    public static ActionResult onUse(PlayerEntity player, World world, Hand hand, BlockHitResult hit) {
        if (!world.isClient() || !ForpakClient.CONFIG.anchorOptimizer) return ActionResult.PASS;
        if (hand != Hand.MAIN_HAND || player.isSpectator() || player.isSneaking()) return ActionResult.PASS;
        if (world.getRegistryKey() == World.NETHER) return ActionResult.PASS; // dans le Nether, l'ancre n'explose pas

        BlockPos pos = hit.getBlockPos();
        BlockState state = world.getBlockState(pos);
        if (!state.isOf(Blocks.RESPAWN_ANCHOR)) return ActionResult.PASS;

        int charges = state.get(RespawnAnchorBlock.CHARGES);
        if (charges == 0) return ActionResult.PASS;
        // Avec de la glowstone en main (et pas encore pleine), on recharge : pas d'explosion
        if (player.getMainHandStack().isOf(Items.GLOWSTONE) && charges < 4) return ActionResult.PASS;

        // Juste après l'envoi du clic au serveur, on retire l'ancre de notre écran
        MinecraftClient.getInstance().execute(() -> {
            if (world.getBlockState(pos).isOf(Blocks.RESPAWN_ANCHOR))
                world.setBlockState(pos, Blocks.AIR.getDefaultState());
        });
        return ActionResult.PASS;
    }
}
