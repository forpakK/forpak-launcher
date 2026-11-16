package fr.forpak.client.hud;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.multiplayer.MultiplayerScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.client.gui.screen.world.SelectWorldScreen;
import net.minecraft.text.Text;

/** Menu principal Minecraft personnalisé Velox (style Lunar / Dawn). */
public class VeloxTitleScreen extends Screen {
    private final Clicks clicks = new Clicks();
    private final String mcVersion = FabricLoader.getInstance().getModContainer("minecraft")
            .map(m -> m.getMetadata().getVersion().getFriendlyString()).orElse("");

    public VeloxTitleScreen() {
        super(Text.literal("Velox Client"));
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        Logo.background(ctx, width, height);
        clicks.poll(client);

        int cx = width / 2;
        int logoSize = Math.min(80, height / 5);
        int y = Logo.draw(ctx, textRenderer, cx, Math.max(8, height / 2 - 150), logoSize) + 16;

        int bw = 210, bh = 26, gap = 6;
        if (Ui.button(ctx, textRenderer, "SOLO", cx - bw / 2, y, bw, bh + 4, mouseX, mouseY, clicks.left, true))
            client.setScreen(new SelectWorldScreen(this));
        y += bh + 4 + gap;
        if (Ui.button(ctx, textRenderer, "MULTIJOUEUR", cx - bw / 2, y, bw, bh + 4, mouseX, mouseY, clicks.left, true))
            client.setScreen(new MultiplayerScreen(this));
        y += bh + 4 + gap;
        if (Ui.button(ctx, textRenderer, "Velox Client", cx - bw / 2, y, bw, bh, mouseX, mouseY, clicks.left, false))
            client.setScreen(new VeloxMenuScreen(this));
        y += bh + gap;
        int half = (bw - gap) / 2;
        if (Ui.button(ctx, textRenderer, "Options", cx - bw / 2, y, half, bh, mouseX, mouseY, clicks.left, false))
            client.setScreen(new OptionsScreen(this, client.options));
        if (Ui.button(ctx, textRenderer, "Quitter", cx - bw / 2 + half + gap, y, half, bh, mouseX, mouseY, clicks.left, false))
            client.scheduleStop();

        String left = "Velox Client · Minecraft " + mcVersion;
        ctx.drawTextWithShadow(textRenderer, left, 4, height - 12, 0xB0FFFFFF);
        String right = "Shift droit en jeu : menu Velox";
        ctx.drawTextWithShadow(textRenderer, right, width - 4 - textRenderer.getWidth(right), height - 12, 0xB0FFFFFF);
    }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public boolean shouldCloseOnEsc() { return false; }
}
