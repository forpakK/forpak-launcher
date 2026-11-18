package fr.forpak.client;

import net.minecraft.client.MinecraftClient;
import net.minecraft.entity.Entity;
import net.minecraft.particle.ParticleEffect;
import net.minecraft.particle.ParticleTypes;

/** Effets de kill (visibles seulement par toi). */
public final class Effects {
    private Effects() { }

    public static final String[] NAMES = {"Aucun", "Éclair", "Flocons", "Étincelles", "Cœurs", "Flammes"};
    public static final String[] DESC = {
            "Pas d'effet",
            "Explosion d'étincelles électriques",
            "Une pluie de flocons de neige",
            "Étincelles vertes et dorées (totem)",
            "Des cœurs qui s'envolent",
            "Une gerbe de feu"
    };

    public static void play(Entity victim) {
        MinecraftClient mc = MinecraftClient.getInstance();
        int mode = ForpakClient.CONFIG == null ? 0 : ForpakClient.CONFIG.killEffect;
        if (mode <= 0 || mc.particleManager == null || victim == null) return;
        double x = victim.getX(), y = victim.getY() + 1.0, z = victim.getZ();
        ParticleEffect main = switch (mode) {
            case 1 -> ParticleTypes.ELECTRIC_SPARK;
            case 2 -> ParticleTypes.SNOWFLAKE;
            case 3 -> ParticleTypes.TOTEM_OF_UNDYING;
            case 4 -> ParticleTypes.HEART;
            default -> ParticleTypes.FLAME;
        };
        java.util.Random r = new java.util.Random();
        int count = mode == 4 ? 14 : 45;
        for (int i = 0; i < count; i++) {
            double a = r.nextDouble() * Math.PI * 2, s = 0.15 + r.nextDouble() * 0.35;
            double vx = Math.cos(a) * s, vz = Math.sin(a) * s, vy = 0.1 + r.nextDouble() * 0.45;
            if (mode == 4) { vx *= 0.3; vz *= 0.3; }
            mc.particleManager.addParticle(main, x, y + r.nextDouble() * 0.8 - 0.3, z, vx, vy, vz);
        }
        if (mode == 1)   // colonne de lumière
            for (int i = 0; i < 25; i++)
                mc.particleManager.addParticle(ParticleTypes.END_ROD, x, y + i * 0.25, z, 0, 0.02, 0);
    }
}
