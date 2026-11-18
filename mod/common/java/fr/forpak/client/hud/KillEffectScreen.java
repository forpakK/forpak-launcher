package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Choix de l'effet de kill. */
public class KillEffectScreen extends Screen {
    private static final String[] NAMES = fr.forpak.client.Effects.NAMES;
    private static final String[] DESC = fr.forpak.client.Effects.DESC;
    private final Screen parent;
    private final Clicks clicks = new Clicks();

    public KillEffectScreen(Screen parent) {
        super(Text.literal("Effet de kill"));
        this.parent = parent;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);
        Config c = ForpakClient.CONFIG;
        int pw = 260, ph = 60 + NAMES.length * 34 + 34, px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, Ui.BG_SOLID);
        Ui.outline(ctx, px, py, pw, ph, 0xFF2A6B5E);
        ctx.drawTextWithShadow(textRenderer, "Effet de kill (visible seulement par toi)", px + 12, py + 12, Ui.WHITE);

        int y = py + 34;
        for (int i = 0; i < NAMES.length; i++) {
            boolean sel = c.killEffect == i;
            boolean hover = Ui.inside(mouseX, mouseY, px + 12, y, pw - 24, 30);
            Ui.rect(ctx, px + 12, y, pw - 24, 30, sel ? 0xFF163D2A : hover ? Ui.CARD_HOVER : Ui.CARD);
            if (sel) Ui.outline(ctx, px + 12, y, pw - 24, 30, Ui.ACCENT);
            ctx.drawTextWithShadow(textRenderer, NAMES[i], px + 20, y + 6, sel ? Ui.ACCENT : Ui.WHITE);
            ctx.drawTextWithShadow(textRenderer, DESC[i], px + 20, y + 17, Ui.GREY);
            if (hover && clicks.left) { c.killEffect = i; c.save(); }
            y += 34;
        }
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
