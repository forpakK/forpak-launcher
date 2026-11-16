package fr.forpak.client;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.entity.Entity;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import org.lwjgl.glfw.GLFW;

import java.util.ArrayDeque;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.Map;

/** Petites stats en direct : CPS, vitesse, reach, combo, totems. */
public final class Stats {
    private Stats() { }

    // --- CPS
    private static final Deque<Long> LEFT = new ArrayDeque<>();
    private static final Deque<Long> RIGHT = new ArrayDeque<>();
    private static boolean leftWas, rightWas;

    public static void pollClicks(MinecraftClient mc) {
        long h = mc.getWindow().getHandle();
        boolean l = GLFW.glfwGetMouseButton(h, GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;
        boolean r = GLFW.glfwGetMouseButton(h, GLFW.GLFW_MOUSE_BUTTON_RIGHT) == GLFW.GLFW_PRESS;
        long now = System.currentTimeMillis();
        if (mc.currentScreen == null) {
            if (l && !leftWas) LEFT.addLast(now);
            if (r && !rightWas) RIGHT.addLast(now);
        }
        leftWas = l;
        rightWas = r;
    }

    public static int cpsLeft() { return trim(LEFT); }
    public static int cpsRight() { return trim(RIGHT); }

    private static int trim(Deque<Long> d) {
        long limit = System.currentTimeMillis() - 1000;
        while (!d.isEmpty() && d.peekFirst() < limit) d.pollFirst();
        return d.size();
    }

    // --- Vitesse (blocs / seconde)
    private static double lastX = Double.NaN, lastZ;
    public static double speed;

    // --- Reach / combo
    public static double lastReach = -1;
    public static long lastReachTime;
    public static int combo;
    private static int lastTargetId = -1;
    private static long lastHitTime;

    // --- Totems (nom du joueur -> nombre de totems utilisés)
    public static final Map<String, Integer> POPS = new LinkedHashMap<>();
    public static final Map<String, Long> POP_TIME = new LinkedHashMap<>();

    public static void tick(MinecraftClient mc) {
        ClientPlayerEntity p = mc.player;
        if (p == null) { lastX = Double.NaN; return; }
        if (!Double.isNaN(lastX)) {
            double dx = p.getX() - lastX, dz = p.getZ() - lastZ;
            double inst = Math.sqrt(dx * dx + dz * dz) * 20.0;
            speed = speed * 0.7 + inst * 0.3;
        }
        lastX = p.getX();
        lastZ = p.getZ();

        // Combo perdu si on se fait taper ou après 2 s sans toucher
        if (p.hurtTime > 0 || System.currentTimeMillis() - lastHitTime > 2000) combo = 0;
    }

    public static void onAttack(PlayerEntity player, Entity target) {
        Vec3d eye = player.getEyePos();
        Box b = target.getBoundingBox();
        double cx = clamp(eye.x, b.minX, b.maxX);
        double cy = clamp(eye.y, b.minY, b.maxY);
        double cz = clamp(eye.z, b.minZ, b.maxZ);
        lastReach = eye.distanceTo(new Vec3d(cx, cy, cz));
        lastReachTime = System.currentTimeMillis();

        long now = System.currentTimeMillis();
        if (target.getId() == lastTargetId && now - lastHitTime < 2000) combo++;
        else combo = 1;
        lastTargetId = target.getId();
        lastHitTime = now;
    }

    public static void onTotemPop(String name) {
        POPS.merge(name, 1, Integer::sum);
        POP_TIME.put(name, System.currentTimeMillis());
    }

    public static void onDeath(String name) {
        POPS.remove(name);
        POP_TIME.remove(name);
    }

    private static double clamp(double v, double min, double max) {
        return v < min ? min : Math.min(v, max);
    }
}
