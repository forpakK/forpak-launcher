package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Options d'un élément du HUD (clic droit sur sa carte) : taille, fond, couleur. */
public class ModuleSettingsScreen extends Screen {
    private static final int[] COLORS = {
            0xFF3EE6A8, 0xFF38BDF8, 0xFF4F8DFF, 0xFFA78BFA, 0xFFF472B6, 0xFFFF5C5C, 0xFFFFA94D, 0xFFFFE066, 0xFFFFFFFF
    };
    private static final String[] COLOR_NAMES = {
            "Vert aurore", "Cyan", "Bleu", "Violet", "Rose", "Rouge", "Orange", "Jaune", "Blanc"
    };

    private final Screen parent;
    private final HudElement element;
    private final Clicks clicks = new Clicks();

    public ModuleSettingsScreen(Screen parent, HudElement element) {
        super(Text.literal(element.name));
        this.parent = parent;
        this.element = element;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);
        Config c = ForpakClient.CONFIG;
        Config.ElementStyle st = c.style(element.id);

        int pw = 260, ph = 196, px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, Ui.BG_SOLID);
        Ui.outline(ctx, px, py, pw, ph, 0xFF2A6B5E);
        ctx.drawTextWithShadow(textRenderer, element.name, px + 12, py + 12, Ui.WHITE);
        ctx.drawTextWithShadow(textRenderer, "Options", px + pw - 12 - textRenderer.getWidth("Options"), py + 12, Ui.GREY);

        int y = py + 34, lx = px + 12, bx = px + pw - 12 - 110;

        // Taille
        ctx.drawTextWithShadow(textRenderer, "Taille", lx, y + 6, Ui.GREY);
        if (Ui.button(ctx, textRenderer, "-", bx, y, 24, 20, mouseX, mouseY, clicks.left, false)) {
            st.scale = Math.max(0.5f, Math.round((st.scale - 0.1f) * 10) / 10f); c.save();
        }
        String pct = Math.round(st.scale * 100) + "%";
        ctx.drawTextWithShadow(textRenderer, pct, bx + 55 - textRenderer.getWidth(pct) / 2, y + 6, Ui.WHITE);
        if (Ui.button(ctx, textRenderer, "+", bx + 86, y, 24, 20, mouseX, mouseY, clicks.left, false)) {
            st.scale = Math.min(2.5f, Math.round((st.scale + 0.1f) * 10) / 10f); c.save();
        }
        y += 28;

        // Fond
        ctx.drawTextWithShadow(textRenderer, "Fond", lx, y + 6, Ui.GREY);
        if (Ui.button(ctx, textRenderer, st.background ? "Activé" : "Désactivé", bx, y, 110, 20, mouseX, mouseY, clicks.left, st.background)) {
            st.background = !st.background; c.save();
        }
        y += 28;

        // Couleur
        int ci = 0;
        for (int i = 0; i < COLORS.length; i++) if (COLORS[i] == st.color) ci = i;
        ctx.drawTextWithShadow(textRenderer, "Couleur", lx, y + 6, Ui.GREY);
        ctx.fill(bx - 16, y + 5, bx - 6, y + 15, st.color);
        if (Ui.button(ctx, textRenderer, COLOR_NAMES[ci], bx, y, 110, 20, mouseX, mouseY, clicks.left, false)) {
            st.color = COLORS[(ci + 1) % COLORS.length]; c.save();
        }
        y += 28;

        // Position
        ctx.drawTextWithShadow(textRenderer, "Position", lx, y + 6, Ui.GREY);
        if (Ui.button(ctx, textRenderer, "Réinitialiser", bx, y, 110, 20, mouseX, mouseY, clicks.left, false)) {
            c.pos.remove(element.id); c.save();
        }

        // Aperçu + retour
        if (Ui.button(ctx, textRenderer, "Retour", px + 12, py + ph - 32, pw - 24, 22, mouseX, mouseY, clicks.left, true))
            close();
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        ForpakClient.CONFIG.save();
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() { return false; }
}
