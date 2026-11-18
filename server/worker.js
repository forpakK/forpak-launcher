// Velox Client API — petit serveur gratuit (Cloudflare Workers + D1).
// - qui joue avec Velox Client (icône à côté du pseudo)
// - page Amis : qui est en jeu et sur quel serveur
// Chaque joueur est vérifié auprès de Mojang (comme un vrai serveur Minecraft) : impossible de se faire passer pour un autre.

const ONLINE_MS = 40 * 60 * 1000;  // icône : 40 min après le dernier signal
const LIVE_MS = 3 * 60 * 1000;     // amis : « en jeu » si signal il y a moins de 3 min
let ready = false;
let cache = { at: 0, body: null };

async function setup(env) {
  if (ready) return;
  await env.DB.exec("CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, seen INTEGER NOT NULL)");
  for (const col of ["name TEXT", "token TEXT", "server TEXT", "live INTEGER"]) {
    try { await env.DB.exec("ALTER TABLE users ADD COLUMN " + col); } catch { /* déjà là */ }
  }
  ready = true;
}

function hex(n) {
  const b = new Uint8Array(n);
  crypto.getRandomValues(b);
  return [...b].map(x => x.toString(16).padStart(2, "0")).join("");
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    await setup(env);
    const now = Date.now();

    // Un joueur Velox se signale (vérifié par Mojang) → reçoit un jeton pour envoyer son statut
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

      const token = hex(24);
      await env.DB.prepare(
        "INSERT INTO users (id, seen, name, token) VALUES (?1, ?2, ?3, ?4) " +
        "ON CONFLICT(id) DO UPDATE SET seen = ?2, name = ?3, token = ?4")
        .bind(id, now, String(profile.name || name), token).run();
      if (Math.random() < 0.05)
        await env.DB.prepare("DELETE FROM users WHERE seen < ?1").bind(now - 30 * 24 * 3600 * 1000).run();
      cache.at = 0;
      return Response.json({ ok: true, token });
    }

    // Statut en direct (serveur actuel), envoyé par le mod toutes les minutes
    if (url.pathname === "/status" && req.method === "POST") {
      let b;
      try { b = await req.json(); } catch { return new Response("bad request", { status: 400 }); }
      const id = String(b.id || "").toLowerCase(), token = String(b.token || "");
      let server = String(b.server || "").slice(0, 100);
      if (!/^[0-9a-f]{32}$/.test(id) || !/^[0-9a-f]{48}$/.test(token)) return new Response("bad request", { status: 400 });
      if (!/^[A-Za-z0-9.\-:_]*$/.test(server)) server = "hidden";
      const res = await env.DB.prepare("UPDATE users SET seen = ?1, live = ?1, server = ?2 WHERE id = ?3 AND token = ?4")
        .bind(now, server, id, token).run();
      if (!res.meta || res.meta.changes === 0) return new Response("bad token", { status: 403 });
      return Response.json({ ok: true });
    }

    // Statut de tes amis (par pseudo)
    if (url.pathname === "/friends") {
      const names = (url.searchParams.get("names") || "").split(",")
        .map(n => n.trim()).filter(n => /^[A-Za-z0-9_]{1,16}$/.test(n)).slice(0, 50);
      if (names.length === 0) return Response.json({ friends: [] });
      const q = "SELECT name, server, live, seen FROM users WHERE lower(name) IN (" + names.map((_, i) => "?" + (i + 1)).join(",") + ")";
      const { results } = await env.DB.prepare(q).bind(...names.map(n => n.toLowerCase())).all();
      const friends = results.map(r => ({
        name: r.name,
        online: !!r.live && now - r.live < LIVE_MS,
        server: r.server || "",
        last: r.live || r.seen
      }));
      return Response.json({ friends, now });
    }

    // Liste des joueurs Velox (pour l'icône)
    if (url.pathname === "/online") {
      if (!cache.body || now - cache.at > 30000) {
        const { results } = await env.DB.prepare("SELECT id FROM users WHERE seen > ?1").bind(now - ONLINE_MS).all();
        cache = { at: now, body: JSON.stringify({ users: results.map(r => r.id) }) };
      }
      return new Response(cache.body, { headers: { "content-type": "application/json" } });
    }

    return new Response("Velox Client API ✓");
  }
};
