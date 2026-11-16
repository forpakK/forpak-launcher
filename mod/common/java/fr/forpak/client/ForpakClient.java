package fr.forpak.client;

import fr.forpak.client.hud.Hud;
import fr.forpak.client.hud.ModulesScreen;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.minecraft.client.MinecraftClient;
import org.lwjgl.glfw.GLFW;

public class ForpakClient implements ClientModInitializer {
    public static Config CONFIG;

    private final boolean[] keyWas = new boolean[GLFW.GLFW_KEY_LAST + 1];
    private boolean zooming = false;
    private int savedFov = 70;
    private boolean savedSmooth = false;

    @Override
    public void onInitializeClient() {
        CONFIG = Config.load();
        HudHook.register();                                   // différent selon la version de MC
        AttackEntityCallback.EVENT.register((player, world, hand, entity, hit) -> {
            if (world.isClient()) Stats.onAttack(player, entity);
            return CrystalOptimizer.onAttack(player, world, hand, entity, hit);
        });
        ClientTickEvents.END_CLIENT_TICK.register(this::onTick);
    }

    /** Vrai une seule fois quand la touche vient d'être appuyée. */
    private boolean pressed(long handle, int key) {
        boolean down = GLFW.glfwGetKey(handle, key) == GLFW.GLFW_PRESS;
        boolean edge = down && !keyWas[key];
        keyWas[key] = down;
        return edge;
    }

    private void onTick(MinecraftClient client) {
        if (client.getWindow() == null) return;
        long handle = client.getWindow().getHandle();
        boolean inGame = client.currentScreen == null && client.player != null;

        // Les touches sont lues à chaque tick pour garder l'état à jour
        boolean menu = pressed(handle, GLFW.GLFW_KEY_RIGHT_SHIFT);
        boolean panic = pressed(handle, GLFW.GLFW_KEY_F8);
        boolean sprint = pressed(handle, GLFW.GLFW_KEY_G);
        boolean sneak = pressed(handle, GLFW.GLFW_KEY_V);

        Stats.tick(client);

        if (inGame) {
            if (menu) client.setScreen(new ModulesScreen());
            if (panic) Hud.hidden = !Hud.hidden;
            if (sprint && CONFIG.toggleSprintModule) { CONFIG.sprintToggled = !CONFIG.sprintToggled; CONFIG.save(); }
            if (sneak && CONFIG.toggleSneakModule) { CONFIG.sneakToggled = !CONFIG.sneakToggled; CONFIG.save(); }
        }

        // ToggleSprint / ToggleSneak
        if (client.player != null) {
            if (CONFIG.toggleSprintModule && CONFIG.sprintToggled) client.options.sprintKey.setPressed(true);
            if (CONFIG.toggleSneakModule && CONFIG.sneakToggled && client.currentScreen == null)
                client.options.sneakKey.setPressed(true);
        }
        if (sprint && !CONFIG.sprintToggled) client.options.sprintKey.setPressed(false);
        if (sneak && !CONFIG.sneakToggled) client.options.sneakKey.setPressed(false);

        // Zoom : maintenir C
        boolean wantZoom = CONFIG.zoom && inGame && GLFW.glfwGetKey(handle, GLFW.GLFW_KEY_C) == GLFW.GLFW_PRESS;
        if (wantZoom && !zooming) {
            zooming = true;
            savedFov = client.options.getFov().getValue();
            savedSmooth = client.options.smoothCameraEnabled;
            client.options.getFov().setValue(Math.max(30, Math.min(CONFIG.zoomFov, savedFov)));
            client.options.smoothCameraEnabled = true;
        } else if (!wantZoom && zooming) {
            zooming = false;
            client.options.getFov().setValue(savedFov);
            client.options.smoothCameraEnabled = savedSmooth;
        }
    }
}
