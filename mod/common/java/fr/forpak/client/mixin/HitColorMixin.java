package fr.forpak.client.mixin;

import fr.forpak.client.ForpakClient;
import net.minecraft.client.render.OverlayTexture;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.ModifyConstant;

/** Hit color : couleur du flash quand une entité prend un coup (rouge par défaut). */
@Mixin(OverlayTexture.class)
public class HitColorMixin {
    @ModifyConstant(method = "<init>", constant = @Constant(intValue = -1308622848))
    private int velox$hitColor(int original) {
        if (ForpakClient.CONFIG == null || !ForpakClient.CONFIG.hitColorEnabled) return original;
        // Minecraft stocke la couleur en ABGR : on convertit notre ARGB
        int argb = ForpakClient.CONFIG.hitColor;
        int a = 0xB2, r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
