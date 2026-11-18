// Velox Client API — petit serveur gratuit (Cloudflare Workers + D1).
// - qui joue avec Velox Client (icône à côté du pseudo)
// - page Amis : qui est en jeu et sur quel serveur, invitations
// - classement de la semaine entre amis, capes choisies
// Chaque joueur est vérifié auprès de Mojang (comme un vrai serveur Minecraft) : impossible de se faire passer pour un autre.

const ONLINE_MS = 40 * 60 * 1000;  // icône : 40 min après le dernier signal
const LIVE_MS = 3 * 60 * 1000;     // amis : « en jeu » si signal il y a moins de 3 min
let ready = false;
let cache = { at: 0, body: null };

async function setup(env) {
  if (ready) return;
  await env.DB.exec("CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, seen INTEGER NOT NULL)");
  await env.DB.exec("CREATE TABLE IF NOT EXISTS invites (id INTEGER PRIMARY KEY AUTOINCREMENT, to_name TEXT, from_name TEXT, server TEXT, at INTEGER)");
  await env.DB.exec("CREATE TABLE IF NOT EXISTS stats (id TEXT, week TEXT, kills INTEGER DEFAULT 0, pops INTEGER DEFAULT 0, crystals INTEGER DEFAULT 0, PRIMARY KEY (id, week))");
  for (const col of ["name TEXT", "token TEXT", "server TEXT", "live INTEGER", "cape TEXT"]) {
    try { await env.DB.exec("ALTER TABLE users ADD COLUMN " + col); } catch { /* déjà là */ }
  }
  ready = true;
}

const CAPES = ["velox", "aurora", "neige", "flamme", "nuit"];
const SPECIAL = ["", "menu", "solo", "private", "hidden"];

function week(now) {   // lundi de la semaine (UTC), ex. 2026-11-16
  const d = new Date(now);
  return new Date(now - ((d.getUTCDay() + 6) % 7) * 86400000).toISOString().slice(0, 10);
}

async function auth(env, b) {   // vérifie id + jeton, renvoie la ligne du joueur
  const id = String(b.id || "").toLowerCase(), token = String(b.token || "");
  if (!/^[0-9a-f]{32}$/.test(id) || !/^[0-9a-f]{48}$/.test(token)) return null;
  return await env.DB.prepare("SELECT * FROM users WHERE id = ?1 AND token = ?2").bind(id, token).first();
}

async function body(req) { try { return await req.json(); } catch { return null; } }

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

      // Le jeton reste le même (le launcher et le jeu peuvent l'utiliser en même temps)
      const old = await env.DB.prepare("SELECT token FROM users WHERE id = ?1").bind(id).first();
      const token = old && old.token ? old.token : hex(24);
      await env.DB.prepare(
        "INSERT INTO users (id, seen, name, token) VALUES (?1, ?2, ?3, ?4) " +
        "ON CONFLICT(id) DO UPDATE SET seen = ?2, name = ?3, token = ?4")
        .bind(id, now, String(profile.name || name), token).run();
      if (Math.random() < 0.05) {
        await env.DB.prepare("DELETE FROM users WHERE seen < ?1").bind(now - 30 * 24 * 3600 * 1000).run();
        await env.DB.prepare("DELETE FROM invites WHERE at < ?1").bind(now - 3600 * 1000).run();
      }
      cache.at = 0;
      return Response.json({ ok: true, token, id, name: String(profile.name || name) });
    }

    // Statut en direct (serveur actuel), envoyé par le mod toutes les minutes
    if (url.pathname === "/status" && req.method === "POST") {
      let b;
      try { b = await req.json(); } catch { return new Response("bad request", { status: 400 }); }
      const id = String(b.id || "").toLowerCase(), token = String(b.token || "");
      let server = String(b.server || "").slice(0, 100);
      if (!/^[0-9a-f]{32}$/.test(id) || !/^[0-9a-f]{48}$/.test(token)) return new Response("bad request", { status: 400 });
      if (!/^[A-Za-z0-9.\-:_]*$/.test(server)) server = "hidden";
      const cape = CAPES.includes(b.cape) ? b.cape : "velox";
      const res = await env.DB.prepare("UPDATE users SET seen = ?1, live = ?1, server = ?2, cape = ?5 WHERE id = ?3 AND token = ?4")
        .bind(now, server, id, token, cape).run();
      if (res.meta && res.meta.changes > 0) cache.at = 0;
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

    // Inviter un ami sur le serveur où je suis
    if (url.pathname === "/invite" && req.method === "POST") {
      const b = await body(req);
      const me = b && await auth(env, b);
      if (!me) return new Response("bad token", { status: 403 });
      const to = String(b.to || "");
      if (!/^[A-Za-z0-9_]{1,16}$/.test(to)) return new Response("bad request", { status: 400 });
      if (!me.live || now - me.live > LIVE_MS || SPECIAL.includes(me.server || ""))
        return Response.json({ ok: false, error: "not_on_server" });
      const last = await env.DB.prepare("SELECT at FROM invites WHERE from_name = ?1 AND lower(to_name) = ?2 ORDER BY at DESC LIMIT 1")
        .bind(me.name, to.toLowerCase()).first();
      if (last && now - last.at < 20000) return Response.json({ ok: false, error: "too_fast" });
      await env.DB.prepare("INSERT INTO invites (to_name, from_name, server, at) VALUES (?1, ?2, ?3, ?4)")
        .bind(to, me.name, me.server, now).run();
      return Response.json({ ok: true, server: me.server });
    }

    // Mes invitations reçues (2 dernières minutes)
    if (url.pathname === "/inbox" && req.method === "POST") {
      const b = await body(req);
      const me = b && await auth(env, b);
      if (!me) return new Response("bad token", { status: 403 });
      const { results } = await env.DB.prepare(
        "SELECT id, from_name, server, at FROM invites WHERE lower(to_name) = ?1 AND at > ?2 ORDER BY at DESC LIMIT 10")
        .bind(String(me.name || "").toLowerCase(), now - 120000).all();
      return Response.json({ invites: results.map(r => ({ id: r.id, from: r.from_name, server: r.server, at: r.at })), now });
    }

    // Stats de la semaine (envoyées par le mod : kills, totems fait sauter, crystals)
    if (url.pathname === "/stats" && req.method === "POST") {
      const b = await body(req);
      const me = b && await auth(env, b);
      if (!me) return new Response("bad token", { status: 403 });
      const n = v => Math.max(0, Math.min(50, Math.floor(Number(v) || 0)));
      const k = n(b.kills), p = n(b.pops), c = n(b.crystals);
      if (k + p + c > 0)
        await env.DB.prepare(
          "INSERT INTO stats (id, week, kills, pops, crystals) VALUES (?1, ?2, ?3, ?4, ?5) " +
          "ON CONFLICT(id, week) DO UPDATE SET kills = kills + ?3, pops = pops + ?4, crystals = crystals + ?5")
          .bind(me.id, week(now), k, p, c).run();
      return Response.json({ ok: true });
    }

    // Classement de la semaine (par pseudo)
    if (url.pathname === "/leaderboard") {
      const names = (url.searchParams.get("names") || "").split(",")
        .map(n => n.trim()).filter(n => /^[A-Za-z0-9_]{1,16}$/.test(n)).slice(0, 51);
      if (names.length === 0) return Response.json({ players: [] });
      const q = "SELECT u.name, s.kills, s.pops, s.crystals FROM users u JOIN stats s ON s.id = u.id AND s.week = ?1 " +
        "WHERE lower(u.name) IN (" + names.map((_, i) => "?" + (i + 2)).join(",") + ")";
      const { results } = await env.DB.prepare(q).bind(week(now), ...names.map(n => n.toLowerCase())).all();
      return Response.json({ week: week(now), players: results });
    }

    // Liste des joueurs Velox (pour l'icône)
    if (url.pathname === "/online") {
      if (!cache.body || now - cache.at > 30000) {
        const { results } = await env.DB.prepare("SELECT id, cape FROM users WHERE seen > ?1").bind(now - ONLINE_MS).all();
        const capes = {};
        for (const r of results) if (r.cape && r.cape !== "velox") capes[r.id] = r.cape;
        cache = { at: now, body: JSON.stringify({ users: results.map(r => r.id), capes }) };
      }
      return new Response(cache.body, { headers: { "content-type": "application/json" } });
    }

    return new Response("Velox Client API ✓");
  }
};
