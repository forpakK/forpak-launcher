package fr.forpak.client.hud;

import fr.forpak.client.Config;
import fr.forpak.client.ForpakClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Profils de HUD : PvP, Survie, Build. Sauvegarder la config actuelle ou la recharger en un clic. */
public class ProfilesScreen extends Screen {
    private static final String[] NAMES = {"PvP", "Survie", "Build"};
    private final Screen parent;
    private final Clicks clicks = new Clicks();
    private String message = "";
    private long messageTime;

    public ProfilesScreen(Screen parent) {
        super(Text.literal("Profils"));
        this.parent = parent;
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        if (client == null) return;
        ctx.fill(0, 0, width, height, 0xA0060A14);
        clicks.poll(client);
        Config c = ForpakClient.CONFIG;

        int pw = 330, ph = 70 + NAMES.length * 36 + 30, px = (width - pw) / 2, py = (height - ph) / 2;
        Ui.rect(ctx, px, py, pw, ph, Ui.BG_SOLID);
        Ui.outline(ctx, px, py, pw, ph, 0xFF2A6B5E);
        ctx.drawTextWithShadow(textRenderer, "Profils de HUD", px + 12, py + 12, Ui.WHITE);
        ctx.drawTextWithShadow(textRenderer, "Modules, positions et styles", px + 12, py + 24, Ui.GREY);

        int y = py + 46;
        for (String name : NAMES) {
            boolean saved = c.profiles.containsKey(name);
            Ui.rect(ctx, px + 12, y, pw - 24, 30, Ui.CARD);
            ctx.drawTextWithShadow(textRenderer, name, px + 22, y + 7, Ui.WHITE);
            String linked = null;
            for (var e : c.serverProfiles.entrySet()) if (e.getValue().equals(name)) linked = e.getKey();
            ctx.drawTextWithShadow(textRenderer, linked != null ? "auto sur " + linked : saved ? "enregistré" : "vide",
                    px + 22, y + 18, linked != null ? Ui.ACCENT : saved ? Ui.GREEN : Ui.GREY);
            String addr = client.getCurrentServerEntry() != null && client.world != null && !client.isInSingleplayer()
                    ? client.getCurrentServerEntry().address.toLowerCase() : null;
            if (saved && addr != null) {
                boolean on = name.equals(c.serverProfiles.get(addr));
                if (Ui.button(ctx, textRenderer, on ? "Délier" : "Ce serveur", px + pw - 12 - 238, y + 5, 66, 20, mouseX, mouseY, clicks.left, false)) {
                    if (on) c.serverProfiles.remove(addr); else c.serverProfiles.put(addr, name);
                    c.save();
                    flash(on ? "Profil délié de " + addr : "Profil " + name + " chargé auto sur " + addr);
                }
            }
            if (Ui.button(ctx, textRenderer, "Sauvegarder", px + pw - 12 - 166, y + 5, 80, 20, mouseX, mouseY, clicks.left, false)) {
                c.saveProfile(name);
                flash("Profil " + name + " sauvegardé");
            }
            if (saved && Ui.button(ctx, textRenderer, "Charger", px + pw - 12 - 80, y + 5, 74, 20, mouseX, mouseY, clicks.left, true)) {
                c.loadProfile(name);
                flash("Profil " + name + " chargé");
            }
            y += 36;
        }

        if (System.currentTimeMillis() - messageTime < 2000)
            ctx.drawTextWithShadow(textRenderer, message, px + (pw - textRenderer.getWidth(message)) / 2, py + ph - 52, Ui.ACCENT);

        if (Ui.button(ctx, textRenderer, "Retour", px + 12, py + ph - 30, pw - 24, 22, mouseX, mouseY, clicks.left, true))
            close();
    }

    private void flash(String m) { message = m; messageTime = System.currentTimeMillis(); }

    @Override
    public void renderBackground(DrawContext ctx, int mouseX, int mouseY, float delta) { }

    @Override
    public void close() {
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() { return false; }
}
