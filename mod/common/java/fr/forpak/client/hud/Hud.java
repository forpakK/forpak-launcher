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
        ELEMENTS.add(new TargetHud());
        ELEMENTS.add(new DeathPoint());
        ELEMENTS.add(new ArmorAlert());
    }

    /** Dessine le HUD. editing = dans l'éditeur (affiche aussi les éléments vides). */
    public static void render(DrawContext ctx, boolean editing) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.player == null) return;
        if (!editing && (hidden || mc.options.hudHidden)) return;

        Stats.pollClicks(mc);
        int sw = ctx.getScaledWindowWidth();
        int sh = ctx.getScaledWindowHeight();
        if (!editing) {
            try { lowHealth(ctx, mc, sw, sh); } catch (Throwable ignored) { }
        }

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
            return mc.textRenderer.getWidth(s) + 12;
        }
        @Override public int height(MinecraftClient mc, boolean editing) { return 15; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            TextRenderer tr = mc.textRenderer;
            String v = value.apply(mc);
            if (v.isEmpty() && editing) v = name;
            Ui.rect(ctx, x, y, width(mc, editing), 15, bg());
            ctx.fill(x + 1, y + 3, x + 3, y + 12, accent());   // petite barre de couleur
            int tx = x + 7;
            if (!label.isEmpty()) {
                ctx.drawTextWithShadow(tr, label, tx, y + 4, accent());
                tx += tr.getWidth(label + " ");
            }
            ctx.drawTextWithShadow(tr, v, tx, y + 4, Ui.WHITE);
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
            Ui.rect(ctx, x, y3, w3, 12, anim("jump", jump));
            ctx.fill(x + w3 / 2 - 14, y3 + 5, x + w3 / 2 + 14, y3 + 7, Ui.WHITE);
        }

        private static final java.util.Map<String, Float> ANIM = new java.util.HashMap<>();

        /** Animation douce quand une touche est appuyée / relâchée. */
        private static int anim(String id, boolean down) {
            float v = ANIM.getOrDefault(id, 0f);
            v += ((down ? 1f : 0f) - v) * 0.35f;
            ANIM.put(id, v);
            int base = bg() == 0 ? 0x30000000 : bg();
            return Ui.lerp(base, pressed(), v);
        }

        private static void key(DrawContext ctx, TextRenderer tr, KeyBinding kb, int x, int y, int w, int h) {
            boolean down = kb.isPressed();
            Ui.rect(ctx, x, y, w, h, anim("k" + System.identityHashCode(kb), down));
            String t = kb.getBoundKeyLocalizedText().getString().toUpperCase();
            if (t.length() > 3) t = t.substring(0, 3);
            ctx.drawTextWithShadow(tr, t, x + (w - tr.getWidth(t)) / 2, y + (h - 8) / 2, Ui.WHITE);
        }

        private static void mouse(DrawContext ctx, TextRenderer tr, boolean down, String label, int cps,
                                  int x, int y, int w, int h) {
            Ui.rect(ctx, x, y, w, h, anim(label, down));
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
                        ctx.drawTextWithShadow(mc.textRenderer, t, x + 52 - 20 - mc.textRenderer.getWidth(t), ry + 3, col);
                        int bw = 26, bxx = x + 52 - 20 - bw;
                        ctx.fill(bxx, ry + 13, bxx + bw, ry + 15, 0x90000000);
                        ctx.fill(bxx, ry + 13, bxx + Math.max(1, bw * pct / 100), ry + 15, col);
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
                out.add(new String[]{name, time, (!e.isInfinite() && secs < 10) ? "1" : "0",
                        String.valueOf(0xFF000000 | e.getEffectType().value().getColor())});
            }
            if (out.isEmpty() && editing) out.add(new String[]{"Force 2", "1:30", "0", String.valueOf(0xFFFFC23D)});
            return out;
        }

        @Override public boolean visible(MinecraftClient mc) { return !mc.player.getStatusEffects().isEmpty(); }

        @Override public int width(MinecraftClient mc, boolean editing) {
            int w = 0;
            for (String[] l : lines(mc, editing)) w = Math.max(w, mc.textRenderer.getWidth(l[0] + "  " + l[1]));
            return w + 18;
        }
        @Override public int height(MinecraftClient mc, boolean editing) { return lines(mc, editing).size() * 14; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            int w = width(mc, editing), ry = y;
            for (String[] l : lines(mc, editing)) {
                Ui.rect(ctx, x, ry, w, 13, bg());
                int dot = (int) Long.parseLong(l[3]);
                ctx.fill(x + 4, ry + 4, x + 9, ry + 9, dot);
                ctx.drawTextWithShadow(mc.textRenderer, l[0], x + 13, ry + 3, Ui.WHITE);
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

    // ----------------------------------------------------------------------- Alerte vie basse

    /** Bords de l'écran qui pulsent en rouge quand il te reste 3 cœurs ou moins. */
    private static void lowHealth(DrawContext ctx, MinecraftClient mc, int sw, int sh) {
        if (!ForpakClient.CONFIG.lowHpWarning || mc.player.isCreative() || mc.player.isSpectator()) return;
        float hp = mc.player.getHealth() + mc.player.getAbsorptionAmount();
        if (hp > 6f || hp <= 0f) return;
        float danger = 1f - hp / 6f;                                   // 0 → 1
        float pulse = 0.65f + 0.35f * (float) Math.sin(System.currentTimeMillis() / 160.0);
        int maxA = (int) (110 * (0.35f + 0.65f * danger) * pulse);
        int bands = 14, step = Math.max(2, Math.min(sw, sh) / 60);
        for (int i = 0; i < bands; i++) {
            int a = maxA * (bands - i) / bands;
            int col = (a << 24) | 0xE01818;
            int o = i * step;
            ctx.fill(o, o, sw - o, o + step, col);                    // haut
            ctx.fill(o, sh - o - step, sw - o, sh - o, col);          // bas
            ctx.fill(o, o + step, o + step, sh - o - step, col);      // gauche
            ctx.fill(sw - o - step, o + step, sw - o, sh - o - step, col); // droite
        }
    }

    // ----------------------------------------------------------------------- Target HUD

    static class TargetHud extends HudElement {
        TargetHud() { super("target", "Target HUD", true, 0.56f, 0.66f); }

        private net.minecraft.entity.LivingEntity target(MinecraftClient mc) {
            if (mc.world == null || Stats.targetId < 0 || System.currentTimeMillis() - Stats.targetTime > 8000) return null;
            var e = mc.world.getEntityById(Stats.targetId);
            return e instanceof net.minecraft.entity.LivingEntity l && l.isAlive() ? l : null;
        }

        @Override public boolean visible(MinecraftClient mc) { return target(mc) != null; }
        @Override public int width(MinecraftClient mc, boolean editing) { return 132; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 36; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            var t = target(mc);
            String name = t != null ? t.getName().getString() : "Adversaire";
            float hp = t != null ? t.getHealth() + t.getAbsorptionAmount() : 14f;
            float max = t != null ? Math.max(1f, t.getMaxHealth()) : 20f;
            double dist = t != null ? mc.player.distanceTo(t) : 3.2;
            float mine = mc.player.getHealth() + mc.player.getAbsorptionAmount();

            Ui.rect(ctx, x, y, 132, 36, bg());
            ctx.fill(x + 1, y + 3, x + 3, y + 33, accent());
            TextRenderer tr = mc.textRenderer;
            if (tr.getWidth(name) > 120) name = tr.trimToWidth(name, 114) + "..";
            ctx.drawTextWithShadow(tr, name, x + 7, y + 4, Ui.WHITE);

            // Barre de vie (dorée si absorption)
            int bw = 118, bx = x + 7, by = y + 15;
            float pct = Math.min(1f, hp / max);
            int col = pct > 0.6f ? Ui.GREEN : pct > 0.3f ? Ui.ORANGE : Ui.RED;
            ctx.fill(bx, by, bx + bw, by + 5, 0x90000000);
            ctx.fill(bx, by, bx + Math.max(1, (int) (bw * pct)), by + 5, col);

            String hpTxt = String.format("%.1f ❤", hp / 2f);
            ctx.drawTextWithShadow(tr, hpTxt, x + 7, y + 24, col);
            String d = String.format("%.1f m", dist);
            ctx.drawTextWithShadow(tr, d, x + 66 - tr.getWidth(d) / 2, y + 24, Ui.GREY);
            // Qui gagne ? (comparaison des vies)
            String w = hp < mine ? "Avantage" : hp > mine ? "Danger" : "Égalité";
            int wc = hp < mine ? Ui.GREEN : hp > mine ? Ui.RED : Ui.ORANGE;
            ctx.drawTextWithShadow(tr, w, x + 125 - tr.getWidth(w), y + 24, wc);
        }
    }

    // ----------------------------------------------------------------------- Point de mort

    static class DeathPoint extends HudElement {
        private static final String[] ARROWS = {"↑", "↗", "→", "↘", "↓", "↙", "←", "↖"};
        DeathPoint() { super("deathpoint", "Point de mort", true, 0.5f, 0.02f); }

        private String text(MinecraftClient mc, boolean editing) {
            double[] p = Stats.deathPos;
            if (p == null || mc.world == null || mc.player.isDead()
                    || !mc.world.getRegistryKey().getValue().toString().equals(Stats.deathDim))
                return editing ? "☠ Mort  120 64 -340 · 85 m ↗" : "";
            double dx = p[0] - mc.player.getX(), dz = p[2] - mc.player.getZ();
            double dist = Math.sqrt(dx * dx + dz * dz);
            if (dist < 3) { Stats.deathPos = null; return ""; }           // arrivé : on efface
            // Direction de la flèche par rapport à là où tu regardes
            double angle = Math.toDegrees(Math.atan2(-dx, dz)) - mc.player.getYaw();
            int idx = Math.floorMod((int) Math.round(angle / 45.0), 8);
            return String.format("☠ Mort  %d %d %d · %d m %s", (int) p[0], (int) p[1], (int) p[2], (int) dist, ARROWS[idx]);
        }

        @Override public boolean visible(MinecraftClient mc) { return !text(mc, false).isEmpty(); }
        @Override public int width(MinecraftClient mc, boolean editing) { return mc.textRenderer.getWidth(text(mc, editing)) + 12; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 15; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            String t = text(mc, editing);
            Ui.rect(ctx, x, y, width(mc, editing), 15, bg());
            ctx.fill(x + 1, y + 3, x + 3, y + 12, Ui.RED);
            ctx.drawTextWithShadow(mc.textRenderer, t, x + 7, y + 4, Ui.WHITE);
        }
    }

    // ----------------------------------------------------------------------- Alerte armure

    static class ArmorAlert extends HudElement {
        private static final String[] NAMES = {"Casque", "Plastron", "Jambières", "Bottes"};
        ArmorAlert() { super("armoralert", "Alerte armure", true, 0.5f, 0.30f); }

        private String text(MinecraftClient mc, boolean editing) {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ARMOR.length; i++) {
                ItemStack s = mc.player.getEquippedStack(ARMOR[i]);
                if (s.isEmpty() || !s.isDamageable()) continue;
                int pct = (s.getMaxDamage() - s.getDamage()) * 100 / Math.max(1, s.getMaxDamage());
                if (pct <= 15) sb.append(sb.length() == 0 ? "" : "  ").append(NAMES[i]).append(' ').append(pct).append('%');
            }
            if (sb.length() == 0) return editing ? "⚠ Casque 9%" : "";
            return "⚠ " + sb;
        }

        @Override public boolean visible(MinecraftClient mc) { return !text(mc, false).isEmpty(); }
        @Override public int width(MinecraftClient mc, boolean editing) { return mc.textRenderer.getWidth(text(mc, editing)) + 12; }
        @Override public int height(MinecraftClient mc, boolean editing) { return 15; }

        @Override public void render(DrawContext ctx, MinecraftClient mc, int x, int y, boolean editing) {
            String t = text(mc, editing);
            boolean blink = (System.currentTimeMillis() / 500) % 2 == 0;
            Ui.rect(ctx, x, y, width(mc, editing), 15, blink ? 0xC0601010 : bg());
            ctx.drawTextWithShadow(mc.textRenderer, t, x + 6, y + 4, blink ? Ui.WHITE : Ui.RED);
        }
    }
}
