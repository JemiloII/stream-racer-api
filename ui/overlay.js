// Transparent OBS browser source, the horizontal bar: racers as avatars along a track line, stacked by place,
// plus RIP / boost / finish effects. The vertical top-N list is its own source, /leaderboard.
// Look & feel comes from the plugin's settings (Settings → Overlay look) and updates live; query params override:
// ?token=  &size=40  &names=0  &accent=%23ff8a00  &spread=34 (min px between cars, 0 = raw positions)
import { createFeed, DEFAULTS, query, esc, pic, ordinal } from "./overlay-shared.js";

let cfg = { ...DEFAULTS };
function applyCfg(saved) {
  cfg = { ...saved };
  if (query.get("size")) cfg.size = +query.get("size");
  if (query.get("spread") != null) cfg.spread = +query.get("spread");
  if (query.get("names") === "0") cfg.names = false;
  if (query.get("accent")) cfg.accent = query.get("accent");
  const root = document.documentElement.style;
  root.setProperty("--size", cfg.size + "px"); root.setProperty("--hazard", cfg.accent); root.setProperty("--line", cfg.line);
  root.setProperty("--line-h", cfg.lineHeight + "px"); root.setProperty("--bottom", cfg.bottom + "px"); root.setProperty("--side", cfg.side + "px");
  document.body.classList.toggle("no-banner", !cfg.banner);
  document.body.classList.toggle("no-names", !cfg.names);
  render();
}

const $racers = document.getElementById("racers");
const $track = document.getElementById("track");
const $banner = document.getElementById("banner");
const feed = createFeed();

// smooth movement: each car keeps a shown progress that chases its latest reported one
const shownPct = new Map(); // login -> {p, tp}
feed.on("pos", (frame) => { for (const [login, , , pct] of frame.v) { const s = shownPct.get(login); if (s) s.tp = pct; else shownPct.set(login, { p: pct, tp: pct }); } });
feed.on("snapshot", (snap) => { if (!snap.vehicles.length) shownPct.clear(); render(); });
feed.on("phase", render);
feed.on("settings", applyCfg);
let last = performance.now();
function loop(now) {
  const k = 1 - Math.exp(-(now - last) / 60); last = now;
  for (const [, s] of shownPct) s.p += (s.tp - s.p) * k;
  render(); requestAnimationFrame(loop);
}
requestAnimationFrame(loop);
const fx = new Map(); // login -> {cls, until}

function render() {
  const vehicles = feed.vehicles();
  const n = vehicles.length;
  const seen = new Set();
  // Min gap by place: cars keep their order but never stack on top of each other (the field bunches at the finish).
  // spread = px between neighbours, -1 = 85% of the avatar size, 0 = raw positions.
  const width = $racers.clientWidth || $track.clientWidth || 0;
  const gap = (cfg.spread ?? -1) < 0 ? cfg.size * 0.85 : cfg.spread;
  const xs = new Map();
  let prevX = Infinity;
  for (const r of [...vehicles].sort((a, b) => a.place - b.place)) {
    const sp = shownPct.get(r.login);
    const raw = ((r.finished ? 100 : (sp ? sp.p : r.pct)) / 100) * width;
    const x = gap > 0 && width > 0 ? Math.max(0, Math.min(raw, prevX - gap)) : raw;
    xs.set(r.login, x); prevX = x;
  }
  for (const r of vehicles) {
    seen.add(r.login);
    let el = $racers.querySelector(`[data-login="${CSS.escape(r.login)}"]`);
    if (!el) {
      el = document.createElement("div"); el.dataset.login = r.login;
      el.innerHTML = `${pic(r)}<span class="place"></span><div class="name">${esc(r.displayName)}</div>`;
      $racers.appendChild(el);
    }
    const f = fx.get(r.login); const fxCls = f && f.until > Date.now() ? " " + f.cls : "";
    el.className = `racer p${r.place}${r.finished ? " finished" : ""}${fxCls}`;
    el.style.setProperty("--c", r.color || "#fff");
    el.style.left = (width > 0 ? xs.get(r.login) + "px" : (r.finished ? 100 : r.pct) + "%");
    el.style.zIndex = n - r.place + 1;
    el.querySelector(".place").textContent = r.place;
  }
  for (const el of [...$racers.children]) if (!seen.has(el.dataset.login)) el.remove();
}

function effect(login, cls, ms) { fx.set(login, { cls, until: Date.now() + ms }); render(); setTimeout(render, ms + 20); }
let bannerTimer;
function banner(html, cls = "") {
  $banner.innerHTML = html; $banner.className = "show " + cls;
  clearTimeout(bannerTimer); bannerTimer = setTimeout(() => ($banner.className = ""), 3000);
}
feed.on("boom", (r) => { banner(`RIP <b>${esc(r.displayName)}</b>`); setTimeout(() => effect(r.login, "boom", 900), 5000); }); // 5 s fuse in-game
feed.on("boost", (r) => effect(r.login, "boosting", 1800));
feed.on("respawn", (r) => effect(r.login, "respawn", 2000));
feed.on("finisher", (r) => banner(`<b>${esc(r.displayName)}</b> finished ${ordinal(r.place)}`, "fin"));

applyCfg(cfg);
feed.start();
