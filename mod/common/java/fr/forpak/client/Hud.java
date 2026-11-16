package fr.forpak.client;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import org.lwjgl.glfw.GLFW;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;

/** Tout l'affichage à l'écran (style Lunar). */
public final class Hud {
    private Hud() { }

    // Couleurs (ARGB, l'alpha est obligatoire en 1.21.6+)
    private static final int BG = 0x90000000;
    private static final int BG_ON = 0xC03DDC84;
    private static final int WHITE = 0xFFFFFFFF;
    private static final int GREEN = 0xFF3DDC84;
    private static final int GREY = 0xFFAAAAAA;

    // CPS
    private static final Deque<Long> LEFT = new ArrayDeque<>();
    private static final Deque<Long> RIGHT = new ArrayDeque<>();
    private static boolean leftWas, rightWas;

    private static final EquipmentSlot[] ARMOR = {
            EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET
    };
    private static final Item[] PVP_ITEMS = {
            Items.END_CRYSTAL, Items.OBSIDIAN, Items.TOTEM_OF_UNDYING, Items.RESPAWN_ANCHOR,
            Items.GLOWSTONE, Items.ENCHANTED_GOLDEN_APPLE, Items.GOLDEN_APPLE, Items.EXPERIENCE_BOTTLE
    };

    public static void render(DrawContext ctx) {
        MinecraftClient mc = MinecraftClient.getInstance();
        ClientPlayerEntity p = mc.player;
        if (p == null || mc.options.hudHidden) return;
        Config c = ForpakClient.CONFIG;
        TextRenderer tr = mc.textRenderer;

        pollClicks(mc);

        // ---- Infos en haut à gauche
        int x = 4, y = 4;
        if (c.fps) y = box(ctx, tr, x, y, "FPS ", String.valueOf(mc.getCurrentFps()));
        if (c.cps) y = box(ctx, tr, x, y, "CPS ", cps(LEFT) + " | " + cps(RIGHT));
        if (c.coords) {
            y = box(ctx, tr, x, y, "XYZ ", String.format("%d %d %d",
                    p.getBlockPos().getX(), p.getBlockPos().getY(), p.getBlockPos().getZ()));
            y = box(ctx, tr, x, y, "Dir ", direction(p.getYaw()));
        }

        // ---- Keystrokes sous les infos
        if (c.keystrokes) keystrokes(ctx, tr, mc, x, y + 4);

        int sw = ctx.getScaledWindowWidth();
        int sh = ctx.getScaledWindowHeight();

        // ---- Armure à droite
        int ry = sh / 2 - 40;
        if (c.armor) {
            for (EquipmentSlot slot : ARMOR) {
                ItemStack s = p.getEquippedStack(slot);
                if (s.isEmpty()) continue;
                int ix = sw - 22;
                ctx.drawItem(s, ix, ry);
                if (s.isDamageable()) {
                    int left = s.getMaxDamage() - s.getDamage();
                    int pct = left * 100 / Math.max(1, s.getMaxDamage());
                    int col = pct > 50 ? GREEN : pct > 20 ? 0xFFFFC23D : 0xFFFF5C5C;
                    String t = String.valueOf(left);
                    ctx.drawTextWithShadow(tr, t, ix - 4 - tr.getWidth(t), ry + 4, col);
                }
                ry += 18;
            }
        }

        // ---- Potions sous l'armure
        if (c.potions) {
            ry += 4;
            List<StatusEffectInstance> effects = new ArrayList<>(p.getStatusEffects());
            for (StatusEffectInstance e : effects) {
                String name = e.getEffectType().value().getName().getString();
                if (e.getAmplifier() > 0) name += " " + (e.getAmplifier() + 1);
                int secs = e.getDuration() / 20;
                String time = e.isInfinite() ? "∞" : String.format("%d:%02d", secs / 60, secs % 60);
                String t = name + "  " + time;
                int w = tr.getWidth(t);
                ctx.fill(sw - w - 8, ry - 2, sw - 2, ry + 10, BG);
                ctx.drawTextWithShadow(tr, name, sw - w - 5, ry, WHITE);
                ctx.drawTextWithShadow(tr, time, sw - 5 - tr.getWidth(time), ry, secs < 10 && !e.isInfinite() ? 0xFFFF5C5C : GREY);
                ry += 14;
            }
        }

        // ---- Compteur crystal PvP (en bas à droite)
        if (c.pvpCounter) {
            int bx = sw - 4;
            int by = sh - 22;
            for (int i = PVP_ITEMS.length - 1; i >= 0; i--) {
                int n = count(p, PVP_ITEMS[i]);
                if (n == 0) continue;
                bx -= 30;
                ctx.fill(bx - 1, by - 1, bx + 28, by + 18, BG);
                ctx.drawItem(new ItemStack(PVP_ITEMS[i]), bx, by);
                String t = String.valueOf(n);
                ctx.drawTextWithShadow(tr, t, bx + 27 - tr.getWidth(t), by + 9, WHITE);
            }
        }
    }

    // ------------------------------------------------------------------

    private static int box(DrawContext ctx, TextRenderer tr, int x, int y, String label, String value) {
        int w = tr.getWidth(label + value);
        ctx.fill(x - 2, y - 2, x + w + 3, y + 10, BG);
        ctx.drawTextWithShadow(tr, label, x, y, GREEN);
        ctx.drawTextWithShadow(tr, value, x + tr.getWidth(label), y, WHITE);
        return y + 14;
    }

    private static void keystrokes(DrawContext ctx, TextRenderer tr, MinecraftClient mc, int x, int y) {
        int s = 22, g = 2;
        key(ctx, tr, mc.options.forwardKey, x + s + g, y, s, s);
        key(ctx, tr, mc.options.leftKey, x, y + s + g, s, s);
        key(ctx, tr, mc.options.backKey, x + s + g, y + s + g, s, s);
        key(ctx, tr, mc.options.rightKey, x + 2 * (s + g), y + s + g, s, s);
        int w3 = 3 * s + 2 * g;
        int half = (w3 - g) / 2;
        int y2 = y + 2 * (s + g);
        mouse(ctx, tr, mc.options.attackKey.isPressed(), "LMB", cps(LEFT), x, y2, half, s);
        mouse(ctx, tr, mc.options.useKey.isPressed(), "RMB", cps(RIGHT), x + half + g, y2, half, s);
        boolean jump = mc.options.jumpKey.isPressed();
        int y3 = y2 + s + g;
        ctx.fill(x, y3, x + w3, y3 + 12, jump ? BG_ON : BG);
        ctx.fill(x + w3 / 2 - 12, y3 + 5, x + w3 / 2 + 12, y3 + 6, jump ? 0xFF000000 : WHITE);
    }

    private static void key(DrawContext ctx, TextRenderer tr, KeyBinding kb, int x, int y, int w, int h) {
        boolean down = kb.isPressed();
        ctx.fill(x, y, x + w, y + h, down ? BG_ON : BG);
        String t = kb.getBoundKeyLocalizedText().getString().toUpperCase();
        if (t.length() > 3) t = t.substring(0, 3);
        ctx.drawTextWithShadow(tr, t, x + (w - tr.getWidth(t)) / 2, y + (h - 8) / 2, down ? 0xFF000000 : WHITE);
    }

    private static void mouse(DrawContext ctx, TextRenderer tr, boolean down, String label, int cps,
                              int x, int y, int w, int h) {
        ctx.fill(x, y, x + w, y + h, down ? BG_ON : BG);
        int col = down ? 0xFF000000 : WHITE;
        ctx.drawTextWithShadow(tr, label, x + (w - tr.getWidth(label)) / 2, y + 3, col);
        String c = cps + " CPS";
        ctx.drawTextWithShadow(tr, c, x + (w - tr.getWidth(c)) / 2, y + 12, down ? 0xFF000000 : GREY);
    }

    private static void pollClicks(MinecraftClient mc) {
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

    private static int cps(Deque<Long> clicks) {
        long limit = System.currentTimeMillis() - 1000;
        while (!clicks.isEmpty() && clicks.peekFirst() < limit) clicks.pollFirst();
        return clicks.size();
    }

    private static int count(ClientPlayerEntity p, Item item) {
        int n = 0;
        var inv = p.getInventory();
        for (int i = 0; i < inv.size(); i++) {
            ItemStack s = inv.getStack(i);
            if (s.isOf(item)) n += s.getCount();
        }
        return n;
    }

    private static String direction(float yaw) {
        String[] dirs = {"S", "SO", "O", "NO", "N", "NE", "E", "SE"};
        int i = Math.floorMod(Math.round(yaw / 45f), 8);
        return dirs[i];
    }
}
