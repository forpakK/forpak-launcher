package fr.forpak.client;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.Perspective;

/** Freelook : maintiens Alt gauche pour regarder autour de toi sans tourner ton perso. */
public final class Freelook {
    private Freelook() { }

    public static boolean active;
    public static float yaw, pitch;
    private static Perspective previous;

    public static void tick(MinecraftClient mc, boolean keyDown) {
        boolean want = keyDown && ForpakClient.CONFIG.freelook && mc.player != null && mc.currentScreen == null;
        if (want && !active) {
            active = true;
            yaw = mc.player.getYaw();
            pitch = mc.player.getPitch();
            previous = mc.options.getPerspective();
            if (previous.isFirstPerson()) mc.options.setPerspective(Perspective.THIRD_PERSON_BACK);
        } else if (!want && active) {
            active = false;
            if (previous != null) mc.options.setPerspective(previous);
        }
    }

    /** Appelé à la place de la rotation du joueur quand le freelook est actif. */
    public static void turn(double dx, double dy) {
        yaw += (float) dx * 0.15f;
        pitch = Math.max(-90f, Math.min(90f, pitch + (float) dy * 0.15f));
    }
}
