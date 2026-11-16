package fr.forpak.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.minecraft.client.MinecraftClient;
import org.lwjgl.glfw.GLFW;

public class ForpakClient implements ClientModInitializer {
    public static Config CONFIG;

    private boolean menuKeyWasDown = false;
    private boolean zooming = false;
    private int savedFov = 70;
    private boolean savedSmooth = false;

    @Override
    public void onInitializeClient() {
        CONFIG = Config.load();
        HudHook.register();                                   // différent selon la version de MC
        AttackEntityCallback.EVENT.register(CrystalOptimizer::onAttack);
        ClientTickEvents.END_CLIENT_TICK.register(this::onTick);
    }

    private void onTick(MinecraftClient client) {
        if (client.getWindow() == null) return;
        long handle = client.getWindow().getHandle();

        // Menu des modules : Shift droit
        boolean menuDown = GLFW.glfwGetKey(handle, GLFW.GLFW_KEY_RIGHT_SHIFT) == GLFW.GLFW_PRESS;
        if (menuDown && !menuKeyWasDown && client.currentScreen == null && client.player != null) {
            client.setScreen(new ModulesScreen());
        }
        menuKeyWasDown = menuDown;

        // Zoom : maintenir C
        boolean wantZoom = CONFIG.zoom && client.currentScreen == null && client.player != null
                && GLFW.glfwGetKey(handle, GLFW.GLFW_KEY_C) == GLFW.GLFW_PRESS;
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
