package fr.forpak.client.hud;

import net.minecraft.client.MinecraftClient;
import org.lwjgl.glfw.GLFW;

/** Détecte les clics gauche / droit (lu directement : marche en 1.21.1 et 1.21.11). */
public final class Clicks {
    private boolean leftWas = true, rightWas = true; // évite un clic "fantôme" à l'ouverture
    public boolean left, right;

    public void poll(MinecraftClient mc) {
        long h = mc.getWindow().getHandle();
        boolean l = GLFW.glfwGetMouseButton(h, GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;
        boolean r = GLFW.glfwGetMouseButton(h, GLFW.GLFW_MOUSE_BUTTON_RIGHT) == GLFW.GLFW_PRESS;
        left = l && !leftWas;
        right = r && !rightWas;
        leftWas = l;
        rightWas = r;
    }
}
