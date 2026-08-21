// RevenantAuthWorker/worker.js
// Сервер авторизации Revenant Launcher на Cloudflare Workers + D1.
// Привязки: DB (D1 database revenant-auth), секрет JWT_SECRET.
// Логи действий видны в Cloudflare: воркер -> Logs (console.log).

const ISSUER = "revenant-auth-server";
const AUDIENCE = "revenant-launcher";
const ACCESS_TTL = 60 * 15;            // 15 мин
const REFRESH_TTL = 60 * 60 * 24 * 30; // 30 дней

// Единые параметры с ASP.NET-сервером
const PBKDF2_ITERATIONS = 100000;       // итерации для новых хешей
const LEGACY_PBKDF2_ITERATIONS = 60000; // старые аккаунты, созданные до перехода
const RATE_LIMIT_WINDOW_MS = 60 * 1000; // окно rate limit
const RATE_LIMIT_MAX = 20;              // 20 запросов/мин с одного IP

const enc = new TextEncoder();
const dec = new TextDecoder();

function b64url(bytes) {
  let s = btoa(String.fromCharCode(...bytes));
  return s.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
function b64urlStr(str) { return b64url(enc.encode(str)); }
function fromB64url(str) {
  str = str.replace(/-/g, "+").replace(/_/g, "/");
  while (str.length % 4) str += "=";
  const bin = atob(str);
  return Uint8Array.from(bin, c => c.charCodeAt(0));
}
function bufToB64(buf) { return btoa(String.fromCharCode(...new Uint8Array(buf))); }

// D1 ненадёжно выполняет сырой многострочный SQL — используем prepare() и одну строку
async function ensureSchema(env) {
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE NOT NULL, password_hash TEXT NOT NULL, salt TEXT NOT NULL, created_at TEXT NOT NULL, last_login TEXT NOT NULL, refresh_token TEXT, refresh_expiry TEXT, last_ip TEXT)"
  ).run();
  // Миграция: колонка last_ip для уже существующих баз
  try { await env.DB.prepare("ALTER TABLE users ADD COLUMN last_ip TEXT").run(); } catch {}
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS login_history (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL, ip TEXT, at TEXT)"
  ).run();
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS play_sessions (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL, started_at TEXT NOT NULL, ended_at TEXT, duration_sec INTEGER NOT NULL DEFAULT 0, version_id TEXT, version_display TEXT, loader TEXT, status TEXT)"
  ).run();
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS friends (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL, friend_id INTEGER NOT NULL, created_at TEXT NOT NULL)"
  ).run();
  // Заявки в друзья: requester_id -> addressee_id, пока не принята
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS friend_requests (id INTEGER PRIMARY KEY AUTOINCREMENT, requester_id INTEGER NOT NULL, addressee_id INTEGER NOT NULL, created_at TEXT NOT NULL)"
  ).run();
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS presence (user_id INTEGER PRIMARY KEY, status TEXT NOT NULL, version TEXT, updated_at TEXT NOT NULL)"
  ).run();
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS notifications (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL, type TEXT NOT NULL, actor_id INTEGER, text TEXT NOT NULL, created_at TEXT NOT NULL, read_at TEXT)"
  ).run();
  await env.DB.prepare(
    "CREATE TABLE IF NOT EXISTS chat_messages (id INTEGER PRIMARY KEY AUTOINCREMENT, sender_id INTEGER NOT NULL, recipient_id INTEGER NOT NULL, text TEXT NOT NULL, created_at TEXT NOT NULL)"
  ).run();
}

// Схема создаётся ОДИН раз на изолят, а не на каждый запрос (лишние операции D1).
// При ошибке сбрасываем промис, чтобы следующая попытка повторилась.
let schemaPromise = null;
function ensureSchemaOnce(env) {
  if (!schemaPromise) {
    schemaPromise = ensureSchema(env).catch((err) => { schemaPromise = null; throw err; });
  }
  return schemaPromise;
}

function clientIp(request) {
  return request.headers.get("cf-connecting-ip") || "";
}

// Rate limit в памяти изолята: 20 запросов/мин с одного IP (как на сервере).
// Лимит per-isolate, но для бесплатного тарифа Workers этого достаточно.
const rateBuckets = new Map();
function isRateLimited(ip) {
  const now = Date.now();
  let bucket = rateBuckets.get(ip);
  if (!bucket || bucket.resetAt <= now) {
    bucket = { count: 0, resetAt: now + RATE_LIMIT_WINDOW_MS };
    rateBuckets.set(ip, bucket);
  }
  bucket.count++;
  if (bucket.count > RATE_LIMIT_MAX) return true;
  // Чистим старые записи, чтобы Map не рос бесконечно
  if (rateBuckets.size > 2000) {
    for (const [k, v] of rateBuckets) if (v.resetAt <= now) rateBuckets.delete(k);
  }
  return false;
}

// Защита от мусорного ввода: тело запроса должно быть объектом, поля — строками
function asString(v) { return typeof v === "string" ? v : ""; }
async function readJson(request) {
  try { return await request.json(); } catch { return null; }
}

async function pbkdf2Hash(password, saltBytes, iterations) {
  const key = await crypto.subtle.importKey("raw", enc.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: saltBytes, iterations }, key, 256);
  return bufToB64(bits);
}
async function hashPassword(password) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const hash = await pbkdf2Hash(password, salt, PBKDF2_ITERATIONS);
  return { hash, salt: bufToB64(salt) };
}
async function verifyPassword(password, hashB64, saltB64) {
  const salt = fromB64url(saltB64);
  const expected = fromB64url(hashB64);
  const actual = fromB64url(await pbkdf2Hash(password, salt, PBKDF2_ITERATIONS));
  if (expected.length !== actual.length) return false;
  let diff = 0;
  for (let i = 0; i < expected.length; i++) diff |= expected[i] ^ actual[i];
  if (diff === 0) return true;
  // Легаси-аккаунты, созданные с 60k итераций, — пробуем старые параметры
  const legacy = fromB64url(await pbkdf2Hash(password, salt, LEGACY_PBKDF2_ITERATIONS));
  if (legacy.length !== expected.length) return false;
  diff = 0;
  for (let i = 0; i < expected.length; i++) diff |= expected[i] ^ legacy[i];
  return diff === 0;
}

async function signJwt(payload, secret) {
  const header = b64urlStr(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const body = b64urlStr(JSON.stringify(payload));
  const data = `${header}.${body}`;
  const key = await crypto.subtle.importKey("raw", enc.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = await crypto.subtle.sign("HMAC", key, enc.encode(data));
  return `${data}.${b64url(new Uint8Array(sig))}`;
}
async function verifyJwt(token, secret) {
  try {
    const [h, b, s] = token.split(".");
    if (!h || !b || !s) return null;
    const data = `${h}.${b}`;
    const key = await crypto.subtle.importKey("raw", enc.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["verify"]);
    const ok = await crypto.subtle.verify("HMAC", key, fromB64url(s), enc.encode(data));
    if (!ok) return null;
    const payload = JSON.parse(dec.decode(fromB64url(b)));
    if (payload.exp && payload.exp * 1000 < Date.now()) return null;
    return payload;
  } catch { return null; }
}

function newRefresh() { return b64url(crypto.getRandomValues(new Uint8Array(48))); }
function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status, headers: { "content-type": "application/json", "access-control-allow-origin": "*" }
  });
}
function corsPreflight() {
  return new Response(null, { headers: { "access-control-allow-origin": "*", "access-control-allow-methods": "GET,POST,OPTIONS", "access-control-allow-headers": "*" } });
}

async function issueTokens(env, user) {
  const secret = env.JWT_SECRET;
  const now = Math.floor(Date.now() / 1000);
  const accessToken = await signJwt({ sub: String(user.id), username: user.username, iss: ISSUER, aud: AUDIENCE, iat: now, exp: now + ACCESS_TTL }, secret);
  const refreshToken = newRefresh();
  const expiry = new Date(Date.now() + REFRESH_TTL * 1000).toISOString();
  await env.DB.prepare("UPDATE users SET refresh_token=?1, refresh_expiry=?2 WHERE id=?3").bind(refreshToken, expiry, user.id).run();
  return { accessToken, refreshToken, userId: user.id, username: user.username };
}

function validateCreds(username, password) {
  const name = asString(username).trim();
  if (!name) return "Введи никнейм";
  if (name.length < 3 || name.length > 16) return "Ник должен быть от 3 до 16 символов";
  // \p{L}\p{N} — любые буквы и цифры Юникода (как char.IsLetterOrDigit на сервере)
  if (!/^[\p{L}\p{N}_]+$/u.test(name)) return "Только буквы, цифры и _";
  if (!asString(password)) return "Введи пароль";
  if (password.length < 6) return "Пароль должен быть минимум 6 символов";
  if (password.length > 64) return "Пароль не может быть длиннее 64 символов";
  return null;
}

// Агрегированная игровая статистика пользователя
async function getPlayerStats(env, userId) {
  const agg = await env.DB.prepare(
    "SELECT COUNT(*) AS cnt, COALESCE(SUM(duration_sec), 0) AS total FROM play_sessions WHERE user_id=?1"
  ).bind(userId).first();
  const fav = await env.DB.prepare(
    "SELECT version_display, COUNT(*) AS c FROM play_sessions WHERE user_id=?1 AND version_display IS NOT NULL GROUP BY version_display ORDER BY c DESC LIMIT 1"
  ).bind(userId).first();
  const last = await env.DB.prepare(
    "SELECT MAX(ended_at) AS last_ended FROM play_sessions WHERE user_id=?1"
  ).bind(userId).first();
  return {
    totalSessions: agg ? Number(agg.cnt) : 0,
    totalSeconds: agg ? Number(agg.total) : 0,
    favoriteVersion: fav && fav.version_display ? fav.version_display : "",
    lastPlayedAt: last && last.last_ended ? last.last_ended : null
  };
}

// Статус отношений с другим пользователем: none | friend | outgoing | incoming
async function getFriendStatus(env, meId, otherId) {
  const accepted = await env.DB.prepare("SELECT 1 FROM friends WHERE user_id=?1 AND friend_id=?2").bind(meId, otherId).first();
  if (accepted) return "friend";
  const outgoing = await env.DB.prepare("SELECT 1 FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(meId, otherId).first();
  if (outgoing) return "outgoing";
  const incoming = await env.DB.prepare("SELECT 1 FROM friend_requests WHERE requester_id=?2 AND addressee_id=?1").bind(meId, otherId).first();
  if (incoming) return "incoming";
  return "none";
}

async function createNotification(env, userId, type, actorId, text) {
  await env.DB.prepare("INSERT INTO notifications(user_id,type,actor_id,text,created_at) VALUES(?1,?2,?3,?4,?5)")
    .bind(userId, type, actorId || null, text, new Date().toISOString()).run();
}

async function getOnlinePresence(env, userId) {
  const row = await env.DB.prepare("SELECT status, version, updated_at FROM presence WHERE user_id=?1").bind(userId).first();
  if (!row) return { isOnline: false, presenceStatus: "offline", currentVersion: null, lastSeenAt: null };
  const fresh = Date.now() - new Date(row.updated_at).getTime() < 90 * 1000;
  return { isOnline: fresh, presenceStatus: fresh ? row.status : "offline", currentVersion: fresh ? row.version : null, lastSeenAt: row.updated_at };
}

async function authUser(request, env) {
  const payload = await verifyJwt((request.headers.get("authorization") || "").replace("Bearer ", ""), env.JWT_SECRET);
  if (!payload) return null;
  return await env.DB.prepare("SELECT * FROM users WHERE id=?1").bind(Number(payload.sub)).first();
}

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") return corsPreflight();
    const url = new URL(request.url);
    const path = url.pathname;
    await ensureSchemaOnce(env);

    // Rate limit на все auth-эндпоинты: 20 запросов/мин с одного IP (как на сервере)
    if (path.startsWith("/api/")) {
      if (isRateLimited(clientIp(request)))
        return json({ message: "Слишком много попыток. Подожди минуту." }, 429);
    }

    if (path === "/health" || path === "/") return json({ status: "ok", name: "Revenant Auth Server" });

    if (path === "/api/auth/register" && request.method === "POST") {
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const { username, password } = body;
      const err = validateCreds(username, password);
      if (err) return json({ message: err }, 400);
      const name = asString(username).trim();
      const exists = await env.DB.prepare("SELECT id FROM users WHERE username=?1").bind(name).first();
      if (exists) return json({ message: "Этот никнейм уже занят" }, 409);
      const { hash, salt } = await hashPassword(password);
      const now = new Date().toISOString();
      const ip = clientIp(request);
      const res = await env.DB.prepare("INSERT INTO users(username,password_hash,salt,created_at,last_login,last_ip) VALUES(?1,?2,?3,?4,?5,?6)").bind(name, hash, salt, now, now, ip).run();
      const user = { id: res.meta.last_row_id, username: name };
      await env.DB.prepare("INSERT INTO login_history(user_id, ip, at) VALUES(?1,?2,?3)").bind(user.id, ip, now).run();
      console.log(`[Auth] Registered: ${name} (id=${user.id})`);
      return json(await issueTokens(env, user));
    }

    if (path === "/api/auth/login" && request.method === "POST") {
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const { username, password } = body;
      const user = await env.DB.prepare("SELECT * FROM users WHERE username=?1").bind(asString(username).trim()).first();
      if (!user || !(await verifyPassword(password, user.password_hash, user.salt))) {
        console.log(`[Auth] Login FAILED: ${(username || "").trim()}`);
        return json({ message: "Неверный никнейм или пароль" }, 401);
      }
      const ip = clientIp(request);
      const now = new Date().toISOString();
      await env.DB.prepare("UPDATE users SET last_login=?1, last_ip=?2 WHERE id=?3").bind(now, ip, user.id).run();
      await env.DB.prepare("INSERT INTO login_history(user_id, ip, at) VALUES(?1,?2,?3)").bind(user.id, ip, now).run();
      console.log(`[Auth] Login: ${user.username}`);
      return json(await issueTokens(env, user));
    }

    if (path === "/api/auth/refresh" && request.method === "POST") {
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const { refreshToken } = body;
      const user = await env.DB.prepare("SELECT * FROM users WHERE refresh_token=?1").bind(asString(refreshToken)).first();
      if (!user || new Date(user.refresh_expiry) < new Date()) return json({ message: "Сессия истекла" }, 401);
      // Смена IP — автовход отклоняем, требуем пароль (ник будет подставлен в лаунчере)
      const ip = clientIp(request);
      if (user.last_ip && ip && user.last_ip !== ip) {
        console.log(`[Auth] Refresh rejected (IP changed): ${user.username} ${user.last_ip} -> ${ip}`);
        return json({ message: "IP изменился — введи пароль" }, 401);
      }
      return json(await issueTokens(env, user));
    }

    if (path === "/api/auth/logout" && request.method === "POST") {
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const { refreshToken } = body;
      await env.DB.prepare("UPDATE users SET refresh_token=NULL, refresh_expiry=NULL WHERE refresh_token=?1").bind(asString(refreshToken)).run();
      console.log(`[Auth] Logout`);
      return json({ message: "ok" });
    }

    if (path === "/api/auth/change-password" && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const currentPassword = asString(body.currentPassword);
      const newPassword = asString(body.newPassword);
      if (!currentPassword || !newPassword) return json({ message: "Заполни оба поля" }, 400);
      if (!(await verifyPassword(currentPassword, user.password_hash, user.salt))) return json({ message: "Неверный текущий пароль" }, 400);
      if (newPassword.length < 6) return json({ message: "Пароль должен быть минимум 6 символов" }, 400);
      if (newPassword.length > 64) return json({ message: "Пароль не может быть длиннее 64 символов" }, 400);
      const { hash, salt } = await hashPassword(newPassword);
      await env.DB.prepare("UPDATE users SET password_hash=?1, salt=?2 WHERE id=?3").bind(hash, salt, user.id).run();
      console.log(`[Auth] Password changed: ${user.username}`);
      return json({ message: "Пароль изменён" });
    }

    if (path === "/api/auth/me" && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const hist = await env.DB.prepare("SELECT ip, at FROM login_history WHERE user_id=?1 ORDER BY id DESC LIMIT 5").bind(user.id).all();
      return json({
        userId: user.id, username: user.username, createdAt: user.created_at, lastLoginAt: user.last_login,
        currentIp: clientIp(request),
        loginHistory: (hist.results || []).map(r => ({ ip: r.ip, at: r.at }))
      });
    }

    // ===== Социальные эндпоинты: сессии, поиск игроков, профили, друзья =====

    // Лаунчер сообщает об окончании игровой сессии
    if (path === "/api/sessions" && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const startedAt = asString(body.startedAt);
      const endedAt = asString(body.endedAt);
      if (!startedAt) return json({ message: "Нет startedAt" }, 400);
      const durationSec = Math.max(0, Math.round((new Date(endedAt || startedAt) - new Date(startedAt)) / 1000));
      await env.DB.prepare(
        "INSERT INTO play_sessions(user_id, started_at, ended_at, duration_sec, version_id, version_display, loader, status) VALUES(?1,?2,?3,?4,?5,?6,?7,?8)"
      ).bind(user.id, startedAt, endedAt || null, durationSec,
        asString(body.versionId) || null, asString(body.versionDisplay) || null,
        asString(body.loader) || null, asString(body.status) || null).run();
      return json({ message: "ok" });
    }

    // Поиск игроков по префиксу ника
    if (path === "/api/users/search" && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const q = (url.searchParams.get("q") || "").trim();
      if (!q) return json({ message: "Укажи запрос q" }, 400);
      const rows = await env.DB.prepare(
        "SELECT id, username FROM users WHERE username LIKE ?1 AND id != ?2 ORDER BY username LIMIT 20"
      ).bind(q + "%", user.id).all();
      const result = [];
      for (const r of (rows.results || [])) {
        const status = await getFriendStatus(env, user.id, r.id);
        result.push({ username: r.username, friendStatus: status, isFriend: status === "friend" });
      }
      return json(result);
    }

    // Публичный профиль игрока со статистикой (без чувствительных данных)
    if (path.startsWith("/api/users/") && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const username = decodeURIComponent(path.substring("/api/users/".length));
      const target = await env.DB.prepare("SELECT * FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      const stats = await getPlayerStats(env, target.id);
      const status = await getFriendStatus(env, user.id, target.id);
      return json({
        username: target.username,
        createdAt: target.created_at,
        friendStatus: status,
        isFriend: status === "friend",
        ...await getOnlinePresence(env, target.id),
        stats
      });
    }

    // Обновить присутствие лаунчера: online/playing. Запись считается свежей 90 секунд.
    if (path === "/api/presence" && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const status = asString(body.status);
      if (status !== "online" && status !== "playing") return json({ message: "Некорректный статус" }, 400);
      const version = asString(body.version) || null;
      await env.DB.prepare("INSERT INTO presence(user_id,status,version,updated_at) VALUES(?1,?2,?3,?4) ON CONFLICT(user_id) DO UPDATE SET status=excluded.status, version=excluded.version, updated_at=excluded.updated_at")
        .bind(user.id, status, version, new Date().toISOString()).run();
      return json({ message: "ok" });
    }

    if (path === "/api/presence" && request.method === "DELETE") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      await env.DB.prepare("DELETE FROM presence WHERE user_id=?1").bind(user.id).run();
      return json({ message: "ok" });
    }

    // Непрочитанные уведомления: заявки, принятые заявки и сообщения.
    if (path === "/api/notifications" && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const unread = url.searchParams.get("unread") === "true";
      const sql = unread
        ? "SELECT n.id,n.type,n.text,n.created_at,n.read_at,u.username AS actor_username FROM notifications n LEFT JOIN users u ON u.id=n.actor_id WHERE n.user_id=?1 AND n.read_at IS NULL ORDER BY n.id DESC LIMIT 50"
        : "SELECT n.id,n.type,n.text,n.created_at,n.read_at,u.username AS actor_username FROM notifications n LEFT JOIN users u ON u.id=n.actor_id WHERE n.user_id=?1 ORDER BY n.id DESC LIMIT 50";
      const rows = await env.DB.prepare(sql).bind(user.id).all();
      return json((rows.results || []).map(n => ({ id: n.id, type: n.type, text: n.text, actorUsername: n.actor_username, createdAt: n.created_at, isRead: !!n.read_at })));
    }

    if (path === "/api/notifications/read" && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const body = await readJson(request) || {};
      const id = Number(body.id || 0);
      if (id > 0)
        await env.DB.prepare("UPDATE notifications SET read_at=?1 WHERE id=?2 AND user_id=?3").bind(new Date().toISOString(), id, user.id).run();
      else
        await env.DB.prepare("UPDATE notifications SET read_at=?1 WHERE user_id=?2 AND read_at IS NULL").bind(new Date().toISOString(), user.id).run();
      return json({ message: "ok" });
    }

    // Чат доступен только между принятыми друзьями.
    if (path.startsWith("/api/chat/") && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const username = decodeURIComponent(path.substring("/api/chat/".length));
      const target = await env.DB.prepare("SELECT id,username FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      const friend = await env.DB.prepare("SELECT 1 FROM friends WHERE user_id=?1 AND friend_id=?2").bind(user.id, target.id).first();
      if (!friend) return json({ message: "Чат доступен только друзьям" }, 403);
      const afterId = Math.max(0, Number(url.searchParams.get("afterId") || 0));
      const rows = await env.DB.prepare("SELECT m.id,m.sender_id,m.recipient_id,m.text,m.created_at,s.username AS sender_username,r.username AS recipient_username FROM chat_messages m JOIN users s ON s.id=m.sender_id JOIN users r ON r.id=m.recipient_id WHERE ((m.sender_id=?1 AND m.recipient_id=?2) OR (m.sender_id=?2 AND m.recipient_id=?1)) AND m.id>?3 ORDER BY m.id ASC LIMIT 100")
        .bind(user.id, target.id, afterId).all();
      await env.DB.prepare("UPDATE notifications SET read_at=?1 WHERE user_id=?2 AND type='message' AND actor_id=?3 AND read_at IS NULL").bind(new Date().toISOString(), user.id, target.id).run();
      return json((rows.results || []).map(m => ({ id: m.id, senderUsername: m.sender_username, recipientUsername: m.recipient_username, text: m.text, createdAt: m.created_at, isMine: m.sender_id === user.id })));
    }

    if (path.startsWith("/api/chat/") && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const username = decodeURIComponent(path.substring("/api/chat/".length));
      const target = await env.DB.prepare("SELECT id,username FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      const friend = await env.DB.prepare("SELECT 1 FROM friends WHERE user_id=?1 AND friend_id=?2").bind(user.id, target.id).first();
      if (!friend) return json({ message: "Чат доступен только друзьям" }, 403);
      const body = await readJson(request);
      const text = asString(body && body.text).trim();
      if (!text) return json({ message: "Напиши сообщение" }, 400);
      if (text.length > 1000) return json({ message: "Сообщение слишком длинное" }, 400);
      const now = new Date().toISOString();
      const inserted = await env.DB.prepare("INSERT INTO chat_messages(sender_id,recipient_id,text,created_at) VALUES(?1,?2,?3,?4)").bind(user.id, target.id, text, now).run();
      const id = Number(inserted.meta.last_row_id);
      await createNotification(env, target.id, "message", user.id, `${user.username}: ${text.length > 80 ? text.substring(0, 80) + "…" : text}`);
      return json({ ok: true, message: { id, senderUsername: user.username, recipientUsername: target.username, text, createdAt: now, isMine: true } });
    }

    // Отправить заявку в друзья (или принять встречную)
    if (path === "/api/friends" && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const body = await readJson(request);
      if (!body) return json({ message: "Некорректный запрос" }, 400);
      const username = asString(body.username).trim();
      if (!username) return json({ message: "Укажи ник" }, 400);
      if (username.toLowerCase() === user.username.toLowerCase()) return json({ message: "Нельзя добавить себя" }, 400);
      const target = await env.DB.prepare("SELECT id FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      const now = new Date().toISOString();

      // Уже друзья
      const accepted = await env.DB.prepare("SELECT 1 FROM friends WHERE user_id=?1 AND friend_id=?2").bind(user.id, target.id).first();
      if (accepted) return json({ message: "Вы уже друзья", status: "friend" });

      // Моя заявка уже висит
      const mine = await env.DB.prepare("SELECT 1 FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(user.id, target.id).first();
      if (mine) return json({ message: "Заявка уже отправлена", status: "outgoing" });

      // Есть встречная заявка от него — сразу становимся друзьями
      const theirs = await env.DB.prepare("SELECT 1 FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).first();
      if (theirs) {
        await env.DB.prepare("DELETE FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).run();
        await env.DB.prepare("INSERT OR IGNORE INTO friends(user_id, friend_id, created_at) VALUES(?1,?2,?3)").bind(user.id, target.id, now).run();
        await env.DB.prepare("INSERT OR IGNORE INTO friends(user_id, friend_id, created_at) VALUES(?1,?2,?3)").bind(target.id, user.id, now).run();
        await createNotification(env, target.id, "friend_accepted", user.id, `${user.username} принял твою заявку в друзья`);
        console.log(`[Social] Friends accepted: ${user.username} <-> ${username}`);
        return json({ message: "Вы теперь друзья!", status: "accepted" });
      }

      await env.DB.prepare("INSERT INTO friend_requests(requester_id, addressee_id, created_at) VALUES(?1,?2,?3)").bind(user.id, target.id, now).run();
      await createNotification(env, target.id, "friend_request", user.id, `${user.username} хочет добавить тебя в друзья`);
      console.log(`[Social] Friend request: ${user.username} -> ${username}`);
      return json({ message: "Заявка отправлена", status: "pending" });
    }

    // Входящие заявки в друзья
    if (path === "/api/friends/requests" && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const rows = await env.DB.prepare(
        "SELECT u.username, r.created_at FROM friend_requests r JOIN users u ON u.id = r.requester_id WHERE r.addressee_id=?1 ORDER BY r.id DESC"
      ).bind(user.id).all();
      return json((rows.results || []).map(r => ({ username: r.username, createdAt: r.created_at })));
    }

    // Принять заявку в друзья
    if (path.startsWith("/api/friends/requests/") && path.endsWith("/accept") && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const rest = path.substring("/api/friends/requests/".length);
      const username = decodeURIComponent(rest.substring(0, rest.length - "/accept".length));
      const target = await env.DB.prepare("SELECT id FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      const req = await env.DB.prepare("SELECT 1 FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).first();
      if (!req) return json({ message: "Заявка не найдена" }, 404);
      const now = new Date().toISOString();
      await env.DB.prepare("DELETE FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).run();
      await env.DB.prepare("INSERT OR IGNORE INTO friends(user_id, friend_id, created_at) VALUES(?1,?2,?3)").bind(user.id, target.id, now).run();
      await env.DB.prepare("INSERT OR IGNORE INTO friends(user_id, friend_id, created_at) VALUES(?1,?2,?3)").bind(target.id, user.id, now).run();
      await createNotification(env, target.id, "friend_accepted", user.id, `${user.username} принял твою заявку в друзья`);
      console.log(`[Social] Request accepted: ${user.username} <-> ${username}`);
      return json({ message: "ok" });
    }

    // Отклонить заявку в друзья (идемпотентно)
    if (path.startsWith("/api/friends/requests/") && path.endsWith("/decline") && request.method === "POST") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const rest = path.substring("/api/friends/requests/".length);
      const username = decodeURIComponent(rest.substring(0, rest.length - "/decline".length));
      const target = await env.DB.prepare("SELECT id FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      await env.DB.prepare("DELETE FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).run();
      return json({ message: "ok" });
    }

    // Убрать из друзей (заодно отменяет любую заявку между нами)
    if (path.startsWith("/api/friends/") && request.method === "DELETE") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const username = decodeURIComponent(path.substring("/api/friends/".length));
      const target = await env.DB.prepare("SELECT id FROM users WHERE username=?1").bind(username).first();
      if (!target) return json({ message: "Игрок не найден" }, 404);
      await env.DB.prepare("DELETE FROM friends WHERE user_id=?1 AND friend_id=?2").bind(user.id, target.id).run();
      await env.DB.prepare("DELETE FROM friends WHERE user_id=?1 AND friend_id=?2").bind(target.id, user.id).run();
      await env.DB.prepare("DELETE FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(user.id, target.id).run();
      await env.DB.prepare("DELETE FROM friend_requests WHERE requester_id=?1 AND addressee_id=?2").bind(target.id, user.id).run();
      return json({ message: "ok" });
    }

    // Список друзей со статистикой и активностью (только принятые)
    if (path === "/api/friends" && request.method === "GET") {
      const user = await authUser(request, env);
      if (!user) return json({ message: "Сессия недействительна" }, 401);
      const rows = await env.DB.prepare(
        "SELECT u.id, u.username, u.created_at FROM friends f JOIN users u ON u.id = f.friend_id WHERE f.user_id=?1 ORDER BY u.username"
      ).bind(user.id).all();
      const result = [];
      for (const r of (rows.results || [])) {
        result.push({
          username: r.username,
          createdAt: r.created_at,
            friendStatus: "friend",
          isFriend: true,
          ...await getOnlinePresence(env, r.id),
          stats: await getPlayerStats(env, r.id)
        });
      }
      return json(result);
    }

    return json({ message: "Not found" }, 404);
  }
};
