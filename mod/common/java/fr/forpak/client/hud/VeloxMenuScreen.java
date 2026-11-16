package fr.forpak.client.hud;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Menu Velox (Shift droit) : logo en haut, gros boutons au centre, comme Lunar. */
public class VeloxMenuScreen extends Screen {
    private final Screen parent;
    private final Clicks clicks = new Clicks();

    public VeloxMenuScreen(Screen parent) {
        super(Text.literal("Velox Client"));
        this.parent = parent;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        if (client.world == null) Logo.background(ctx, width, height);
        else ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);

        int cx = width / 2;
        int logoSize = Math.min(72, height / 6);
        int y = Logo.draw(ctx, textRenderer, cx, Math.max(10, height / 2 - 120), logoSize) + 14;

        int bw = 220, bh = 34;
        if (Ui.button(ctx, textRenderer, "MODULES", cx - bw / 2, y, bw, bh + 6, mouseX, mouseY, clicks.left, true))
            client.setScreen(new ModulesScreen(this));
        y += bh + 14;
        if (Ui.button(ctx, textRenderer, "DÉPLACER LE HUD", cx - bw / 2, y, bw, bh, mouseX, mouseY, clicks.left, true))
            client.setScreen(new HudEditorScreen(this));
        y += bh + 10;
        int half = (bw - 8) / 2;
        if (Ui.button(ctx, textRenderer, "Profils", cx - bw / 2, y, half, 24, mouseX, mouseY, clicks.left, false))
            client.setScreen(new ProfilesScreen(this));
        if (Ui.button(ctx, textRenderer, "Fermer", cx - bw / 2 + half + 8, y, half, 24, mouseX, mouseY, clicks.left, false))
            close();

        String hint = "Clic gauche : activer · Clic droit : options";
        ctx.drawTextWithShadow(textRenderer, hint, cx - textRenderer.getWidth(hint) / 2, y + 36, Ui.GREY);
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() { return false; }
}
