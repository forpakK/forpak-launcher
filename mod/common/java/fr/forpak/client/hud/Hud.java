package fr.forpak.client.hud;

import fr.forpak.client.ForpakClient;
import fr.forpak.client.Stats;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.util.math.MathHelper;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;

/** Tous les éléments du HUD (style Lunar). */
public final class Hud {
    private Hud() { }

    public static boolean hidden = false; // F8 : tout cacher

    // Style de l'élément en cours de dessin (taille, fond, couleur)
    static fr.forpak.client.Config.ElementStyle current;
    static int bg() { return current == null || current.background ? Ui.BG : 0x00000000; }
    static int accent() { return current == null ? Ui.ACCENT : current.color; }
    static int pressed() { return (accent() & 0x00FFFFFF) | 0xC8000000; }
    public static final List<HudElement> ELEMENTS = new ArrayList<>();
    private static final java.util.Set<String> FAILED = new java.util.HashSet<>();

    private static final EquipmentSlot[] ARMOR = {
            EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET
    };
    private static final Item[] PVP_ITEMS = {
            Items.END_CRYSTAL, Items.OBSIDIAN, Items.TOTEM_OF_UNDYING, Items.RESPAWN_ANCHOR,
            Items.GLOWSTONE, Items.ENCHANTED_GOLDEN_APPLE, Items.GOLDEN_APPLE, Items.EXPERIENCE_BOTTLE
    };
    private static final DateTimeFormatter CLOCK = DateTimeFormatter.ofPattern("HH:mm");

    static {
        ELEMENTS.add(text("fps", "FPS", true, 0.005f, 0.01f, "FPS", mc -> String.valueOf(mc.getCurrentFps())));
        ELEMENTS.add(text("cps", "CPS", true, 0.005f, 0.05f, "CPS", mc -> Stats.cpsLeft() + " | " + Stats.cpsRight()));
        ELEMENTS.add(text("ping", "Ping", true, 0.005f, 0.09f, "Ping", Hud::ping));
        ELEMENTS.add(text("coords", "Coordonnées", true, 0.005f, 0.13f, "XYZ", mc -> {
            ClientPlayerEntity p = mc.player;
            return p == null ? "0 0 0" : p.getBlockPos().getX() + " " + p.getBlockPos().getY() + " " + p.getBlockPos().getZ();
        }));
        ELEMENTS.add(text("speed", "Vitesse", false, 0.005f, 0.17f, "Vitesse", mc -> String.format("%.2f m/s", Stats.speed)));
        ELEMENTS.add(text("clock", "Horloge", false, 0.005f, 0.21f, "Heure", mc -> LocalTime.now().format(CLOCK)));
        ELEMENTS.add(text("saturation", "Saturation", false, 0.005f, 0.25f, "Saturation", mc ->
                mc.player == null ? "0" : String.format("%.1f", mc.player.getHungerManager().getSaturationLevel())));
        ELEMENTS.add(new Keystrokes());
        ELEMENTS.add(new TextElement("reach", "Reach", true, 0.56f, 0.50f, "Reach",
                mc -> Stats.lastReach < 0 ? "0.00" : String.format("%.2f", Stats.lastReach)) {
            @Override public boolean visible(MinecraftClient mc) {
                return Stats.lastReach >= 0 && System.currentTimeMillis() - Stats.lastReachTime < 3000;
            }
        });
        ELEMENTS.add(new TextElement("combo", "Combo", true, 0.56f, 0.55f, "Combo", mc -> String.valueOf(Stats.combo)) {
            @Override public boolean visible(MinecraftClient mc) { return Stats.combo > 1; }
        });
        ELEMENTS.add(new TextElement("kb", "Indicateur KB", true, 0.56f, 0.60f, "KB", mc ->
                (Stats.lastHitSprint ? "§aSprint hit" : "§cSans sprint") + " §7(" + Stats.sprintPercent() + "% W-tap)") {
            @Override public boolean visible(MinecraftClient mc) {
                return System.currentTimeMillis() - Stats.lastHitTimeKb < 2500;
            }
        });
        ELEMENTS.add(text("session", "Stats de session", false, 0.005f, 0.90f, "Session", mc -> Stats.sessionText()));
        ELEMENTS.add(new TextElement("toggles", "Statut Sprint", true, 0.005f, 0.96f, "", Hud::toggleText) {
            @Override public boolean visible(MinecraftClient mc) { return !toggleText(mc).isEmpty(); }
        });
        ELEMENTS.add(new Compass());
        ELEMENTS.add(new Armor());
        ELEMENTS.add(new Potions());
        ELEMENTS.add(new PvpCounter());
        ELEMENTS.add(new Totems());
    }

    /** Dessine le HUD. editing = dans l'éditeur (affiche aussi les éléments vides). */
    public static void render(DrawContext ctx, boolean editing) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.player == null) return;
        if (!editing && (hidden || mc.options.hudHidden)) return;

        Stats.pollClicks(mc);
        int sw = ctx.getScaledWindowWidth();
        int sh = ctx.getScaledWindowHeight();

        for (HudElement e : ELEMENTS) {
            if (!e.isEnabled() || FAILED.contains(e.id)) continue;
            try {
                if (!editing && !e.visible(mc)) continue;
            } catch (Throwable t) {
                FAILED.add(e.id);
                continue;
            }
            current = ForpakClient.CONFIG.style(e.id);
            float scale = Math.max(0.5f, Math.min(2.5f, current.scale));
            int w = Math.round(e.width(mc, editing) * scale), h = Math.round(e.height(mc, editing) * scale);
            if (w <= 0 || h <= 0) continue;
            float[] p = e.position();
            int x = Math.round(p[0] * Math.max(0, sw - w));
            int y = Math.round(p[1] * Math.max(0, sh - h));
            e.lastX = x; e.lastY = y; e.lastW = w; e.lastH = h;
            fr.forpak.client.HudHook.push(ctx, x, y, scale);
            try {
                e.render(ctx, mc, 0, 0, editing);
            } catch (Throwable t) {
                // Sécurité : un élément qui bug est ignoré au lieu de faire planter le jeu
                if (FAILED.add(e.id)) System.err.println("[Velox] Élément HUD " + e.id + " désactivé : " + t);
            } finally {
                fr.forpak.client.HudHook.pop(ctx);
            }
        }
        current = null;
    }

    // ======================================================================= éléments

    private static TextElement text(String id, String name, boolean on, float x, float y, String label,
                                    Function<MinecraftClient, String> value) {
        return new TextElement(id, name, on, x, y, label, value);
    }

    public static class TextElement extends HudElement {
        private final String label;
        private final Function<MinecraftClient, String> value;

        TextElement(String id, String name, boolean on, float x, float y, String label, Function<MinecraftClient, String> value) {
            super(id, name, on, x, y);
            this.label = label;
            this.value = value;
        }

        private String full(MinecraftClient mc) {
            String v = value.apply(mc);
            return label.isEmpty() ? v : label + " " + v;
        }

        @Override public int width(MinecraftClient mc, boolean editing) {
            String s = full(mc);
            if (s.isEmpty() && editing) s = name;
            return mc.textRenderer.getWidth(s) + 8;
        }
        @Override public int height(MinecraftClient mc, boolean editing) { return 13; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            TextRenderer tr = mc.textRenderer;
            String v = value.apply(mc);
            if (v.isEmpty() && editing) v = name;
            Ui.rect(ctx, x, y, width(mc, editing), 13, bg());
            int tx = x + 4;
            if (!label.isEmpty()) {
                ctx.drawTextWithShadow(tr, label, tx, y + 3, accent());
                tx += tr.getWidth(label + " ");
            }
            ctx.drawTextWithShadow(tr, v, tx, y + 3, Ui.WHITE);
        }
    }

    private static String ping(MinecraftClient mc) {
        if (mc.getNetworkHandler() == null || mc.player == null) return "0 ms";
        PlayerListEntry e = mc.getNetworkHandler().getPlayerListEntry(mc.player.getUuid());
        return (e == null ? 0 : e.getLatency()) + " ms";
    }

    private static String toggleText(MinecraftClient mc) {
        var c = ForpakClient.CONFIG;
        if (c.sneakToggled && c.toggleSneakModule) return "[Sneak (Toggled)]";
        if (c.sprintToggled && c.toggleSprintModule) return "[Sprint (Toggled)]";
        return "";
    }

    // ----------------------------------------------------------------------- Keystrokes

    static class Keystrokes extends HudElement {
        private static final int S = 22, G = 2;
        Keystrokes() { super("keystrokes", "Keystrokes", true, 0.005f, 0.30f); }

        @Override public int width(MinecraftClient mc, boolean editing) { return 3 * S + 2 * G; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 3 * S + 3 * G + 12; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            TextRenderer tr = mc.textRenderer;
            key(ctx, tr, mc.options.forwardKey, x + S + G, y, S, S);
            key(ctx, tr, mc.options.leftKey, x, y + S + G, S, S);
            key(ctx, tr, mc.options.backKey, x + S + G, y + S + G, S, S);
            key(ctx, tr, mc.options.rightKey, x + 2 * (S + G), y + S + G, S, S);
            int w3 = 3 * S + 2 * G, half = (w3 - G) / 2, y2 = y + 2 * (S + G);
            mouse(ctx, tr, mc.options.attackKey.isPressed(), "LMB", Stats.cpsLeft(), x, y2, half, S);
            mouse(ctx, tr, mc.options.useKey.isPressed(), "RMB", Stats.cpsRight(), x + half + G, y2, half, S);
            boolean jump = mc.options.jumpKey.isPressed();
            int y3 = y2 + S + G;
            Ui.rect(ctx, x, y3, w3, 12, jump ? pressed() : bg());
            ctx.fill(x + w3 / 2 - 14, y3 + 5, x + w3 / 2 + 14, y3 + 7, Ui.WHITE);
        }

        private static void key(DrawContext ctx, TextRenderer tr, KeyBinding kb, int x, int y, int w, int h) {
            boolean down = kb.isPressed();
            Ui.rect(ctx, x, y, w, h, down ? pressed() : bg());
            String t = kb.getBoundKeyLocalizedText().getString().toUpperCase();
            if (t.length() > 3) t = t.substring(0, 3);
            ctx.drawTextWithShadow(tr, t, x + (w - tr.getWidth(t)) / 2, y + (h - 8) / 2, Ui.WHITE);
        }

        private static void mouse(DrawContext ctx, TextRenderer tr, boolean down, String label, int cps,
                                  int x, int y, int w, int h) {
            Ui.rect(ctx, x, y, w, h, down ? pressed() : bg());
            ctx.drawTextWithShadow(tr, label, x + (w - tr.getWidth(label)) / 2, y + 3, Ui.WHITE);
            String c = cps + " CPS";
            ctx.drawTextWithShadow(tr, c, x + (w - tr.getWidth(c)) / 2, y + 12, down ? Ui.WHITE : Ui.GREY);
        }
    }

    // ----------------------------------------------------------------------- Boussole

    static class Compass extends HudElement {
        private static final String[] DIRS = {"S", "SO", "O", "NO", "N", "NE", "E", "SE"};
        Compass() { super("compass", "Boussole", true, 0.5f, 0.005f); }

        @Override public int width(MinecraftClient mc, boolean editing) { return 170; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 14; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int w = 170, cx = x + w / 2;
            Ui.rect(ctx, x, y, w, 14, bg());
            float yaw = mc.player.getYaw();
            for (int i = 0; i < 8; i++) {
                float rel = MathHelper.wrapDegrees(i * 45f - yaw);
                if (Math.abs(rel) > 80) continue;
                int px = cx + Math.round(rel / 80f * (w / 2f - 8));
                String d = DIRS[i];
                int col = d.length() == 1 ? Ui.WHITE : Ui.GREY;
                ctx.drawTextWithShadow(mc.textRenderer, d, px - mc.textRenderer.getWidth(d) / 2, y + 3, col);
            }
            ctx.fill(cx, y + 11, cx + 1, y + 14, accent());
        }
    }

    // ----------------------------------------------------------------------- Armure

    static class Armor extends HudElement {
        Armor() { super("armor", "Armure", true, 0.985f, 0.40f); }

        @Override public int width(MinecraftClient mc, boolean editing) { return 52; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 4 * 18; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int ry = y;
            for (EquipmentSlot slot : ARMOR) {
                ItemStack s = mc.player.getEquippedStack(slot);
                if (!s.isEmpty()) {
                    ctx.drawItem(s, x + 52 - 17, ry);
                    if (s.isDamageable()) {
                        int left = s.getMaxDamage() - s.getDamage();
                        int pct = left * 100 / Math.max(1, s.getMaxDamage());
                        int col = pct > 50 ? accent() : pct > 20 ? Ui.ORANGE : Ui.RED;
                        String t = String.valueOf(left);
                        ctx.drawTextWithShadow(mc.textRenderer, t, x + 52 - 20 - mc.textRenderer.getWidth(t), ry + 4, col);
                    }
                } else if (editing) {
                    Ui.rect(ctx, x + 52 - 17, ry, 16, 16, bg());
                }
                ry += 18;
            }
        }
    }

    // ----------------------------------------------------------------------- Potions

    static class Potions extends HudElement {
        Potions() { super("potions", "Potions", true, 0.985f, 0.62f); }

        private List<String[]> lines(MinecraftClient mc, boolean editing) {
            List<String[]> out = new ArrayList<>();
            for (StatusEffectInstance e : mc.player.getStatusEffects()) {
                String name = e.getEffectType().value().getName().getString();
                if (e.getAmplifier() > 0) name += " " + (e.getAmplifier() + 1);
                int secs = e.getDuration() / 20;
                String time = e.isInfinite() ? "∞" : String.format("%d:%02d", secs / 60, secs % 60);
                out.add(new String[]{name, time, (!e.isInfinite() && secs < 10) ? "1" : "0"});
            }
            if (out.isEmpty() && editing) out.add(new String[]{"Force 2", "1:30", "0"});
            return out;
        }

        @Override public boolean visible(MinecraftClient mc) { return !mc.player.getStatusEffects().isEmpty(); }

        @Override public int width(MinecraftClient mc, boolean editing) {
            int w = 0;
            for (String[] l : lines(mc, editing)) w = Math.max(w, mc.textRenderer.getWidth(l[0] + "  " + l[1]));
            return w + 8;
        }
        @Override public int height(MinecraftClient mc, boolean editing) { return lines(mc, editing).size() * 14; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int w = width(mc, editing), ry = y;
            for (String[] l : lines(mc, editing)) {
                Ui.rect(ctx, x, ry, w, 13, bg());
                ctx.drawTextWithShadow(mc.textRenderer, l[0], x + 4, ry + 3, Ui.WHITE);
                ctx.drawTextWithShadow(mc.textRenderer, l[1], x + w - 4 - mc.textRenderer.getWidth(l[1]), ry + 3,
                        l[2].equals("1") ? Ui.RED : Ui.GREY);
                ry += 14;
            }
        }
    }

    // ----------------------------------------------------------------------- Compteur crystal PvP

    static class PvpCounter extends HudElement {
        PvpCounter() { super("pvpcounter", "Compteur PvP", true, 0.99f, 0.985f); }

        private List<Item> shown(MinecraftClient mc, boolean editing) {
            List<Item> out = new ArrayList<>();
            for (Item it : PVP_ITEMS) if (editing || count(mc, it) > 0) out.add(it);
            return out;
        }

        @Override public boolean visible(MinecraftClient mc) { return !shown(mc, false).isEmpty(); }
        @Override public int width(MinecraftClient mc, boolean editing) { return shown(mc, editing).size() * 30; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 20; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int bx = x;
            for (Item it : shown(mc, editing)) {
                Ui.rect(ctx, bx, y, 28, 20, bg());
                ctx.drawItem(new ItemStack(it), bx + 2, y + 2);
                String t = String.valueOf(count(mc, it));
                ctx.drawTextWithShadow(mc.textRenderer, t, bx + 27 - mc.textRenderer.getWidth(t), y + 11, Ui.WHITE);
                bx += 30;
            }
        }

        private static int count(MinecraftClient mc, Item item) {
            int n = 0;
            var inv = mc.player.getInventory();
            for (int i = 0; i < inv.size(); i++) {
                ItemStack s = inv.getStack(i);
                if (s.isOf(item)) n += s.getCount();
            }
            return n;
        }
    }

    // ----------------------------------------------------------------------- Totems utilisés

    static class Totems extends HudElement {
        Totems() { super("totems", "Totems adverses", true, 0.99f, 0.08f); }

        private List<String> lines(boolean editing) {
            List<String> out = new ArrayList<>();
            long now = System.currentTimeMillis();
            for (Map.Entry<String, Integer> e : Stats.POPS.entrySet()) {
                Long t = Stats.POP_TIME.get(e.getKey());
                if (t != null && now - t < 60000) out.add(e.getKey() + " §c-" + e.getValue() + " totem" + (e.getValue() > 1 ? "s" : ""));
            }
            if (out.size() > 4) out = out.subList(out.size() - 4, out.size());
            if (out.isEmpty() && editing) out.add("Joueur §c-2 totems");
            return out;
        }

        @Override public boolean visible(MinecraftClient mc) { return !lines(false).isEmpty(); }

        @Override public int width(MinecraftClient mc, boolean editing) {
            int w = 0;
            for (String l : lines(editing)) w = Math.max(w, mc.textRenderer.getWidth(l));
            return w + 8;
        }
        @Override public int height(MinecraftClient mc, boolean editing) { return lines(editing).size() * 14; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int w = width(mc, editing), ry = y;
            for (String l : lines(editing)) {
                Ui.rect(ctx, x, ry, w, 13, bg());
                ctx.drawTextWithShadow(mc.textRenderer, l, x + 4, ry + 3, Ui.WHITE);
                ry += 14;
            }
        }
    }
}
