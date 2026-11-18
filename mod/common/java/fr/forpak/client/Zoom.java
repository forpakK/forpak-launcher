package fr.forpak.client;

/** Zoom fluide façon Lunar : maintenir C, molette pour zoomer plus ou moins. */
public final class Zoom {
    private Zoom() { }

    public static volatile boolean active = false;
    private static double level = 4.0;        // x4 par défaut
    private static double current = 1.0;      // facteur affiché (lissé)
    private static long last = System.nanoTime();

    public static void start() {
        if (!active) level = Math.max(2.0, Math.min(12.0, ForpakClient.CONFIG == null ? 4.0 : ForpakClient.CONFIG.zoomLevel));
        active = true;
    }

    public static void stop() { active = false; }

    /** Molette pendant le zoom : plus ou moins de zoom. */
    public static void scroll(double amount) {
        level = Math.max(1.5, Math.min(30.0, level * (amount > 0 ? 1.18 : 1 / 1.18)));
    }

    /** Multiplie le champ de vision (appelé à chaque image). */
    public static double apply(double fov) {
        long now = System.nanoTime();
        double dt = Math.min(0.1, (now - last) / 1e9);
        last = now;
        double target = active ? level : 1.0;
        current += (target - current) * (1 - Math.exp(-dt * 14));   // lissage
        if (Math.abs(current - target) < 0.001) current = target;
        return current <= 1.0001 ? fov : fov / current;
    }
}
