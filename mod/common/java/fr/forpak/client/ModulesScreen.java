package fr.forpak.client;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;

import java.util.function.Consumer;
import java.util.function.Supplier;

/** Menu des modules (Shift droit). */
public class ModulesScreen extends Screen {

    public ModulesScreen() {
        super(Text.literal("Forpak Client"));
    }

    @Override
    protected void init() {
        Config c = ForpakClient.CONFIG;
        int bw = 150, bh = 20, gap = 4;
        int cols = 2;
        int startX = width / 2 - (bw * cols + gap) / 2;
        int startY = height / 2 - 70;

        Object[][] mods = {
                {"FPS", (Supplier<Boolean>) () -> c.fps, (Consumer<Boolean>) v -> c.fps = v},
                {"CPS", (Supplier<Boolean>) () -> c.cps, (Consumer<Boolean>) v -> c.cps = v},
                {"Coordonnées", (Supplier<Boolean>) () -> c.coords, (Consumer<Boolean>) v -> c.coords = v},
                {"Keystrokes", (Supplier<Boolean>) () -> c.keystrokes, (Consumer<Boolean>) v -> c.keystrokes = v},
                {"Armure", (Supplier<Boolean>) () -> c.armor, (Consumer<Boolean>) v -> c.armor = v},
                {"Potions", (Supplier<Boolean>) () -> c.potions, (Consumer<Boolean>) v -> c.potions = v},
                {"Compteur PvP", (Supplier<Boolean>) () -> c.pvpCounter, (Consumer<Boolean>) v -> c.pvpCounter = v},
                {"Crystal Optimizer", (Supplier<Boolean>) () -> c.crystalOptimizer, (Consumer<Boolean>) v -> c.crystalOptimizer = v},
                {"Zoom (C)", (Supplier<Boolean>) () -> c.zoom, (Consumer<Boolean>) v -> c.zoom = v},
        };

        for (int i = 0; i < mods.length; i++) {
            String name = (String) mods[i][0];
            @SuppressWarnings("unchecked") Supplier<Boolean> get = (Supplier<Boolean>) mods[i][1];
            @SuppressWarnings("unchecked") Consumer<Boolean> set = (Consumer<Boolean>) mods[i][2];
            int x = startX + (i % cols) * (bw + gap);
            int y = startY + (i / cols) * (bh + gap);
            addDrawableChild(ButtonWidget.builder(label(name, get.get()), b -> {
                set.accept(!get.get());
                b.setMessage(label(name, get.get()));
                c.save();
            }).dimensions(x, y, bw, bh).build());
        }

        int rows = (mods.length + cols - 1) / cols;
        addDrawableChild(ButtonWidget.builder(Text.literal("Fermer"), b -> close())
                .dimensions(width / 2 - 50, startY + rows * (bh + gap) + 10, 100, bh).build());
    }

    private static Text label(String name, boolean on) {
        return Text.literal(name + " : " + (on ? "§aON" : "§cOFF"));
    }

    @Override
    public void render(DrawContext ctx, int mouseX, int mouseY, float delta) {
        super.render(ctx, mouseX, mouseY, delta);
        ctx.drawCenteredTextWithShadow(textRenderer, Text.literal("§aFORPAK §fCLIENT"), width / 2, height / 2 - 92, 0xFFFFFFFF);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
