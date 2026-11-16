package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Choix de la couleur du flash quand on tape une entité. */
public class HitColorScreen extends Screen {
    private static final int[] COLORS = {0xFF3EE6A8, 0xFF38BDF8, 0xFFA78BFA, 0xFFF472B6, 0xFFFF5C5C, 0xFFFFA94D, 0xFFFFE066, 0xFFFFFFFF};
    private static final String[] NAMES = {"Vert aurore", "Cyan", "Violet", "Rose", "Rouge", "Orange", "Jaune", "Blanc"};
    private final Screen parent;
    private final Clicks clicks = new Clicks();
    private final int startColor;

    public HitColorScreen(Screen parent) {
        super(Text.literal("Hit color"));
        this.parent = parent;
        this.startColor = ForpakClient.CONFIG.hitColor;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);
        Config c = ForpakClient.CONFIG;
        int pw = 260, ph = 190, px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, Ui.BG_SOLID);
        Ui.outline(ctx, px, py, pw, ph, 0xFF2A6B5E);
        ctx.drawTextWithShadow(textRenderer, "Couleur des coups", px + 12, py + 12, Ui.WHITE);

        int y = py + 34;
        for (int i = 0; i < COLORS.length; i++) {
            int x = px + 12 + (i % 4) * 60, yy = y + (i / 4) * 34;
            boolean sel = c.hitColor == COLORS[i];
            boolean hover = Ui.inside(mouseX, mouseY, x, yy, 54, 28);
            Ui.rect(ctx, x, yy, 54, 28, hover ? Ui.CARD_HOVER : Ui.CARD);
            ctx.fill(x + 4, yy + 4, x + 14, yy + 24, COLORS[i]);
            ctx.drawTextWithShadow(textRenderer, NAMES[i].split(" ")[0], x + 17, yy + 10, sel ? Ui.ACCENT : Ui.WHITE);
            if (sel) Ui.outline(ctx, x, yy, 54, 28, Ui.ACCENT);
            if (hover && clicks.left) { c.hitColor = COLORS[i]; c.hitColorEnabled = true; c.save(); }
        }
        String note = c.hitColor != startColor ? "Redémarre Minecraft pour appliquer" : "Appliqué au lancement du jeu";
        ctx.drawTextWithShadow(textRenderer, note, px + (pw - textRenderer.getWidth(note)) / 2, py + ph - 46, Ui.GREY);
        if (Ui.button(ctx, textRenderer, "Retour", px + 12, py + ph - 30, pw - 24, 22, mouseX, mouseY, clicks.left, true))
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
