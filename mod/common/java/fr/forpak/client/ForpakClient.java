package fr.forpak.client;

import fr.forpak.client.hud.Hud;
import fr.forpak.client.hud.VeloxMenuScreen;
import fr.forpak.client.hud.VeloxTitleScreen;
import net.fabricmc.fabric.api.event.player.UseBlockCallback;
import net.minecraft.client.gui.screen.TitleScreen;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.minecraft.client.MinecraftClient;
import org.lwjgl.glfw.GLFW;

public class ForpakClient implements ClientModInitializer {
    public static Config CONFIG;

    private final boolean[] keyWas = new boolean[GLFW.GLFW_KEY_LAST + 1];
    private boolean zooming = false;
    private String lastAddress = "";
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
        UseBlockCallback.EVENT.register(AnchorOptimizer::onUse);
        UseBlockCallback.EVENT.register((player, world, hand, hit) -> {
            // Compteur de crystals posés (stats de session)
            if (world.isClient() && hand == net.minecraft.util.Hand.MAIN_HAND
                    && player.getMainHandStack().isOf(net.minecraft.item.Items.END_CRYSTAL)) {
                var b = world.getBlockState(hit.getBlockPos());
                if (b.isOf(net.minecraft.block.Blocks.OBSIDIAN) || b.isOf(net.minecraft.block.Blocks.BEDROCK))
                {
                    Stats.sessionCrystals++;
                    VeloxUsers.pendingCrystals.incrementAndGet();
                }
            }
            return net.minecraft.util.ActionResult.PASS;
        });
        Capes.loadList();
        VeloxUsers.start();
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
        boolean join = pressed(handle, GLFW.GLFW_KEY_J);

        Stats.tick(client);
        Freelook.tick(client, GLFW.glfwGetKey(handle, GLFW.GLFW_KEY_LEFT_ALT) == GLFW.GLFW_PRESS);

        // Menu principal Velox à la place de l'écran titre de Minecraft
        if (CONFIG.customMenu && client.currentScreen instanceof TitleScreen)
            client.setScreen(new VeloxTitleScreen());

        if (inGame) {
            if (menu) client.setScreen(new VeloxMenuScreen(null));
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

        // Zoom fluide : maintenir C (molette = plus ou moins de zoom)
        boolean wantZoom = CONFIG.zoom && inGame && GLFW.glfwGetKey(handle, GLFW.GLFW_KEY_C) == GLFW.GLFW_PRESS;
        if (wantZoom && !zooming) {
            zooming = true;
            savedSmooth = client.options.smoothCameraEnabled;
            client.options.smoothCameraEnabled = true;
            Zoom.start();
        } else if (!wantZoom && zooming) {
            zooming = false;
            client.options.smoothCameraEnabled = savedSmooth;
            Zoom.stop();
        }

        // Invitation reçue : J pour rejoindre
        if (inGame && join) VeloxUsers.acceptInvite(client);

        // Profil de HUD lié au serveur : chargé automatiquement en arrivant
        String addr = client.getCurrentServerEntry() != null && client.world != null && !client.isInSingleplayer()
                ? client.getCurrentServerEntry().address.toLowerCase() : "";
        if (!addr.equals(lastAddress)) {
            lastAddress = addr;
            String prof = CONFIG.serverProfiles.get(addr);
            if (!addr.isEmpty() && prof != null && CONFIG.loadProfile(prof))
                Toasts.show("Profil " + prof, "Chargé pour " + addr, fr.forpak.client.hud.Ui.ACCENT, 4000);
        }
    }
}
