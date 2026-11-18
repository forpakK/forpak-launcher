// Velox Client API — petit serveur gratuit (Cloudflare Workers + D1).
// Il sait qui joue avec Velox Client pour afficher l'icône à côté du pseudo.
// Aucune donnée perso : seulement l'UUID Minecraft et l'heure du dernier signal.
// Le joueur est vérifié auprès de Mojang (comme un vrai serveur Minecraft) : impossible de tricher.

const ONLINE_MS = 40 * 60 * 1000;   // considéré « en ligne » 40 min après son dernier signal
let cache = { at: 0, body: null };  // petit cache en mémoire (30 s)

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    await env.DB.exec("CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, seen INTEGER NOT NULL)");

    // Un joueur Velox se signale
    if (url.pathname === "/ping" && req.method === "POST") {
      let b;
      try { b = await req.json(); } catch { return new Response("bad request", { status: 400 }); }
      const name = String(b.name || ""), serverId = String(b.serverId || "");
      if (!/^[A-Za-z0-9_]{1,16}$/.test(name) || !/^[0-9a-f]{16,64}$/.test(serverId))
        return new Response("bad request", { status: 400 });

      const r = await fetch("https://sessionserver.mojang.com/session/minecraft/hasJoined?username="
        + encodeURIComponent(name) + "&serverId=" + serverId);
      if (r.status !== 200) return new Response("not verified", { status: 403 });
      const profile = await r.json();
      const id = String(profile.id || "").toLowerCase();
      if (!/^[0-9a-f]{32}$/.test(id)) return new Response("not verified", { status: 403 });

      const now = Date.now();
      await env.DB.prepare("INSERT INTO users (id, seen) VALUES (?1, ?2) ON CONFLICT(id) DO UPDATE SET seen = ?2")
        .bind(id, now).run();
      if (Math.random() < 0.05)   // ménage de temps en temps
        await env.DB.prepare("DELETE FROM users WHERE seen < ?1").bind(now - 7 * 24 * 3600 * 1000).run();
      cache.at = 0;
      return Response.json({ ok: true });
    }

    // Liste des joueurs Velox en ligne
    if (url.pathname === "/online") {
      if (!cache.body || Date.now() - cache.at > 30000) {
        const { results } = await env.DB.prepare("SELECT id FROM users WHERE seen > ?1")
          .bind(Date.now() - ONLINE_MS).all();
        cache = { at: Date.now(), body: JSON.stringify({ users: results.map(r => r.id) }) };
      }
      return new Response(cache.body, { headers: { "content-type": "application/json" } });
    }

    return new Response("Velox Client API ✓");
  }
};
