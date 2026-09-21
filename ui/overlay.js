// Transparent OBS overlay: racers as avatars along a track line, stacked by place, plus RIP / boost / finish effects.
// Look & feel comes from the plugin's settings (Settings tab → Overlay) and updates live; query params override:
// ?token=  &size=40  &board=10 (top-N list, 0 = off)  &names=0  &accent=%23ff8a00
const q = new URLSearchParams(location.search);
const token = q.get("token") || "";
const DEFAULTS = { size: 40, board: 10, names: true, accent: "#ffd400", line: "rgba(255,255,255,.35)", lineHeight: 6, bottom: 28, side: 24, boardSide: "left", banner: true, boardScale: 1 };
let cfg = { ...DEFAULTS };
function applyCfg(o) {
  cfg = { ...DEFAULTS, ...(o || {}) };
  if (q.get("size")) cfg.size = +q.get("size");
  if (q.get("board") != null) cfg.board = +q.get("board");
  if (q.get("names") === "0") cfg.names = false;
  if (q.get("accent")) cfg.accent = q.get("accent");
  const r = document.documentElement.style;
  r.setProperty("--size", cfg.size + "px"); r.setProperty("--hazard", cfg.accent); r.setProperty("--line", cfg.line);
  r.setProperty("--line-h", cfg.lineHeight + "px"); r.setProperty("--bottom", cfg.bottom + "px"); r.setProperty("--side", cfg.side + "px");
  r.setProperty("--board-scale", cfg.boardScale);
  document.body.classList.toggle("board-right", cfg.boardSide === "right");
  document.body.classList.toggle("no-banner", !cfg.banner);
  render();
}

const $racers = document.getElementById("racers");
const $board = document.getElementById("board");
const $banner = document.getElementById("banner");
const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const initials = (r) => esc((r.displayName || r.login || "?").slice(0, 2).toUpperCase());
const pic = (r, cls) => r.avatar ? `<img class="pic ${cls}" src="${esc(r.avatar)}" alt="">` : `<div class="pic init ${cls}">${initials(r)}</div>`;

let snap = { vehicles: [] };
const shownPct = new Map(); // login -> {p, tp}
function applyPos(frame) {
  const byLogin = new Map(snap.vehicles.map((v) => [v.login, v]));
  for (const [login, x, z, pct, place, fin] of frame.v) {
    const v = byLogin.get(login); if (!v) continue;
    v.pct = pct; v.place = place; v.finished = !!fin;
    const s = shownPct.get(login); if (s) s.tp = pct; else shownPct.set(login, { p: pct, tp: pct });
  }
  snap.vehicles.sort((a, b) => a.place - b.place);
}
let last = performance.now();
function loop(now) {
  const k = 1 - Math.exp(-(now - last) / 60); last = now;
  for (const [, s] of shownPct) s.p += (s.tp - s.p) * k;
  render(); requestAnimationFrame(loop);
}
requestAnimationFrame(loop);
const fx = new Map(); // login -> {cls, until}

function render() {
  const n = snap.vehicles.length;
  const seen = new Set();
  for (const r of snap.vehicles) {
    seen.add(r.login);
    let el = $racers.querySelector(`[data-login="${CSS.escape(r.login)}"]`);
    if (!el) {
      el = document.createElement("div"); el.dataset.login = r.login;
      el.innerHTML = `${pic(r, "")}<span class="place"></span><div class="name">${esc(r.displayName)}</div>`;
      $racers.appendChild(el);
    }
    const f = fx.get(r.login); const fxCls = f && f.until > Date.now() ? " " + f.cls : "";
    el.className = `racer p${r.place}${r.finished ? " finished" : ""}${fxCls}`;
    el.style.setProperty("--c", r.color || "#fff");
    const sp = shownPct.get(r.login);
    el.style.left = (r.finished ? 100 : (sp ? sp.p : r.pct)) + "%";
    el.style.zIndex = n - r.place + 1;
    el.querySelector(".place").textContent = r.place;
  }
  for (const el of [...$racers.children]) if (!seen.has(el.dataset.login)) el.remove();
  document.body.classList.toggle("no-names", !cfg.names);

  $board.innerHTML = cfg.board > 0 ? snap.vehicles.slice(0, cfg.board).map((r) => `
    <div class="row" style="--c:${esc(r.color || "#fff")}"><span class="n">${r.place}</span>${pic(r, "")}
      <span class="nm">${esc(r.displayName)}<small>${r.finished ? "FIN" : r.pct.toFixed(0) + "%"}</small></span></div>`).join("") : "";
}

function effect(login, cls, ms) { fx.set(login, { cls, until: Date.now() + ms }); render(); setTimeout(render, ms + 20); }
let bannerTimer;
function banner(html, cls = "") {
  $banner.innerHTML = html; $banner.className = "show " + cls;
  clearTimeout(bannerTimer); bannerTimer = setTimeout(() => ($banner.className = ""), 3000);
}

let es, retry;
function connect() {
  clearTimeout(retry); es?.close();
  es = new EventSource("/events" + (token ? "?token=" + encodeURIComponent(token) : ""));
  for (const ev of ["lobby", "race_start", "positions", "race_end"]) es.addEventListener(ev, (e) => { snap = JSON.parse(e.data); });
  es.addEventListener("pos", (e) => applyPos(JSON.parse(e.data)));
  es.addEventListener("boom", (e) => { const r = JSON.parse(e.data); banner(`RIP <b>${esc(r.displayName)}</b>`); setTimeout(() => effect(r.login, "boom", 900), 5000); }); // 5 s fuse in-game
  es.addEventListener("boost", (e) => effect(JSON.parse(e.data).login, "boosting", 1800));
  es.addEventListener("respawn", (e) => effect(JSON.parse(e.data).login, "respawn", 2000));
  es.addEventListener("joined", resync);
  es.addEventListener("settings", (e) => applyCfg(JSON.parse(e.data).overlay));
  es.addEventListener("finisher", (e) => { const r = JSON.parse(e.data); banner(`<b>${esc(r.displayName)}</b> finished ${r.place === 1 ? "1st" : r.place === 2 ? "2nd" : r.place === 3 ? "3rd" : r.place + "th"}`, "fin"); });
  // Chromium quietly stops retrying after a few refused connections (a game restart takes ~35 s),
  // so we never rely on its retry: every error closes the stream and we reopen it ourselves, forever.
  es.onopen = () => { console.log("[overlay] events connected"); resync(); };
  es.onerror = () => { console.log("[overlay] events lost, retrying in 3s"); es.close(); retry = setTimeout(connect, 3000); };
}
const T = token ? "?token=" + encodeURIComponent(token) : "";
function resync() {
  fetch("/race" + T).then((r) => r.json()).then((s) => { snap = s; render(); }).catch(() => {});
  fetch("/settings" + T).then((r) => r.json()).then((s) => applyCfg(s.overlay)).catch(() => {});
}
applyCfg({});
resync();
connect();
