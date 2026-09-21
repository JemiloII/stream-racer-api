// Browser-source mini map: the route outline + car dots, drawn on a canvas that fills the page.
// Same look settings as the in-game map (Settings → Mini map), live via the settings event.
// Query: ?token=  &names=1 (labels)  &track=%23fff  &bg=%23000  &alpha=0.55  &marker=3
const q = new URLSearchParams(location.search);
const token = q.get("token") || "";
const T = token ? "?token=" + encodeURIComponent(token) : "";
const DEFAULTS = { marker: 3, bg: "#000000", alpha: 0.55, track: "#ffffff", pad: 1.15, leaderBig: true, names: true, aspect: "1:1" };
// The canvas fills the window; the map box is the largest rectangle of the chosen aspect that fits, centred.
// Size the OBS source to that ratio and the box fills it exactly.
let W = 400, H = 300, OX = 0, OY = 0;
let cfg = { ...DEFAULTS };

const canvas = document.getElementById("map");
const ctx = canvas.getContext("2d");
let snap = { vehicles: [] }, route = [], bounds = null;
// smooth movement: each car keeps a shown position that chases its latest reported one
const shown = new Map(); // login -> {x, z, tx, tz}
function applyPos(frame) {
  const byLogin = new Map(snap.vehicles.map((v) => [v.login, v]));
  for (const [login, x, z, pct, place, fin] of frame.v) {
    const v = byLogin.get(login); if (!v) continue;
    v.x = x; v.z = z; v.pct = pct; v.place = place; v.finished = !!fin;
    const s = shown.get(login); if (s) { s.tx = x; s.tz = z; } else shown.set(login, { x, z, tx: x, tz: z });
  }
  snap.vehicles.sort((a, b) => a.place - b.place);
}
let last = performance.now();
function loop(now) {
  const k = 1 - Math.exp(-(now - last) / 60); last = now; // ~60 ms ease
  for (const [login, s] of shown) { s.x += (s.tx - s.x) * k; s.z += (s.tz - s.z) * k; }
  draw(); requestAnimationFrame(loop);
}
requestAnimationFrame(loop);

function applyCfg(o) {
  cfg = { ...DEFAULTS, ...(o || {}) };
  for (const k of ["track", "bg", "alpha", "marker", "pad"]) if (q.get(k)) cfg[k] = k === "track" || k === "bg" ? q.get(k) : +q.get(k);
  if (q.get("names") != null) cfg.names = q.get("names") === "1";
  if (q.get("leaderBig") != null) cfg.leaderBig = q.get("leaderBig") === "1";
  if (q.get("aspect")) cfg.aspect = q.get("aspect");
  fit();
}

function ratio() {
  const a = String(cfg.aspect || "16:9").trim().toLowerCase();
  if (a === "auto" && bounds) return Math.max(0.2, (bounds.maxX - bounds.minX) / Math.max(1, bounds.maxZ - bounds.minZ));
  const m = /^(\d+(?:\.\d+)?)\s*[:x\/]\s*(\d+(?:\.\d+)?)$/.exec(a);
  return m ? +m[1] / +m[2] : 16 / 9;
}
function fit() {
  const dpr = devicePixelRatio || 1;
  canvas.style.width = innerWidth + "px"; canvas.style.height = innerHeight + "px";
  canvas.width = innerWidth * dpr; canvas.height = innerHeight * dpr;
  const r = ratio();
  W = innerWidth; H = W / r;
  if (H > innerHeight) { H = innerHeight; W = H * r; }
  OX = (innerWidth - W) / 2; OY = (innerHeight - H) / 2;
  ctx.setTransform(dpr, 0, 0, dpr, OX * dpr, OY * dpr);
  draw();
}
addEventListener("resize", fit);

function project() {
  if (!bounds) return null;
  const bw = Math.max(1, bounds.maxX - bounds.minX) * cfg.pad, bh = Math.max(1, bounds.maxZ - bounds.minZ) * cfg.pad;
  const s = Math.min(W / bw, H / bh);
  const cx = (bounds.minX + bounds.maxX) / 2, cz = (bounds.minZ + bounds.maxZ) / 2;
  return (x, z) => [W / 2 + (x - cx) * s, H / 2 - (z - cz) * s]; // z up on screen, like looking down with north up
}

function hexA(hex, a) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex || "");
  if (!m) return `rgba(0,0,0,${a})`;
  const n = parseInt(m[1], 16);
  return `rgba(${n >> 16 & 255},${n >> 8 & 255},${n & 255},${a})`;
}

function draw() {
  ctx.clearRect(-OX, -OY, innerWidth, innerHeight);
  ctx.fillStyle = hexA(cfg.bg, cfg.alpha);
  roundRect(0, 0, W, H, 10); ctx.fill();
  const P = project();
  if (!P || route.length < 2) return;
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  ctx.strokeStyle = cfg.track; ctx.lineWidth = Math.max(2, H * 0.018);
  ctx.beginPath();
  route.forEach(([x, z], i) => { const [px, py] = P(x, z); i ? ctx.lineTo(px, py) : ctx.moveTo(px, py); });
  ctx.stroke();
  const leader = snap.vehicles.find((v) => !v.finished) || snap.vehicles[0];
  const base = H * cfg.marker / 100;
  const dots = [];
  for (const v of [...snap.vehicles].reverse()) { // draw 1st last so it sits on top
    if (v.x == null) continue;
    const s = shown.get(v.login);
    const [px, py] = P(s ? s.x : v.x, s ? s.z : v.z);
    const r = base / 2 * (cfg.leaderBig && v === leader ? 1.6 : 1) * (v.finished ? 0.6 : 1);
    ctx.beginPath(); ctx.arc(px, py, r, 0, Math.PI * 2);
    ctx.fillStyle = v.color || "#fff"; ctx.fill();
    ctx.lineWidth = Math.max(1, r * 0.25); ctx.strokeStyle = "rgba(0,0,0,.7)"; ctx.stroke();
    dots.push({ v, px, py, r });
  }
  if (cfg.names) labels(dots, base);
}

// Names beside their dot (right, or left near the edge), pushed apart vertically until nothing overlaps.
function labels(dots, base) {
  const fs = Math.max(9, base * 0.9), lineH = fs * 1.15, gap = base * 0.7;
  ctx.font = `600 ${fs}px "Chakra Petch", sans-serif`; ctx.textBaseline = "middle";
  const placed = [];
  for (const d of [...dots].sort((a, b) => a.py - b.py)) {
    const text = d.v.displayName || d.v.login;
    const w = ctx.measureText(text).width + 4;
    const flip = d.px + gap + w > W - 4;
    const x0 = flip ? d.px - gap - w : d.px + gap, x1 = x0 + w;
    let y = Math.min(Math.max(d.py, fs * 0.6), H - fs * 0.6);
    let moved = true, guard = 0;
    while (moved && guard++ < 20) {
      moved = false;
      for (const o of placed) if (x0 < o.x1 && x1 > o.x0 && Math.abs(y - o.y) < lineH) { y = o.y + lineH; moved = true; }
    }
    placed.push({ x0, x1, y });
    ctx.textAlign = flip ? "right" : "left";
    const tx = flip ? d.px - gap : d.px + gap;
    ctx.lineWidth = 3; ctx.strokeStyle = "rgba(0,0,0,.85)"; ctx.lineJoin = "round"; ctx.strokeText(text, tx, y);
    ctx.fillStyle = d.v.color || "#fff"; ctx.fillText(text, tx, y);
  }
  ctx.textBaseline = "alphabetic";
}
function roundRect(x, y, w, h, r) { ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r); ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath(); }

async function loadTrack() {
  try { const t = await (await fetch("/track" + T)).json(); route = t.points || []; bounds = t.bounds || null; } catch { route = []; bounds = null; }
  fit();
}
function resync() {
  fetch("/race" + T).then((r) => r.json()).then((s) => { snap = s; }).catch(() => {});
  fetch("/settings" + T).then((r) => r.json()).then((s) => applyCfg(s.minimap)).catch(() => {});
  loadTrack();
}

let es, retry;
function connect() {
  clearTimeout(retry); es?.close();
  es = new EventSource("/events" + T);
  for (const ev of ["lobby", "race_start", "positions"]) es.addEventListener(ev, (e) => { snap = JSON.parse(e.data); for (const v of snap.vehicles) if (v.x != null && !shown.has(v.login)) shown.set(v.login, { x: v.x, z: v.z, tx: v.x, tz: v.z }); });
  // race over: clear the map (keep the outline)
  es.addEventListener("race_end", () => { snap = { vehicles: [] }; shown.clear(); });
  es.addEventListener("lobby", () => { shown.clear(); });
  es.addEventListener("pos", (e) => applyPos(JSON.parse(e.data)));
  es.addEventListener("race_start", loadTrack);
  es.addEventListener("lobby", loadTrack);
  es.addEventListener("joined", () => fetch("/race" + T).then((r) => r.json()).then((s) => { snap = s; }));
  es.addEventListener("settings", (e) => applyCfg(JSON.parse(e.data).minimap));
  es.onopen = resync;
  es.onerror = () => { es.close(); retry = setTimeout(connect, 3000); };
}
applyCfg({}); resync(); connect();
