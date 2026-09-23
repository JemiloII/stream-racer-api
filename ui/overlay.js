// Transparent OBS browser source, the horizontal bar: racers as avatars along a track line, stacked by place,
// plus RIP / boost / finish effects. The vertical top-N list is its own source, /leaderboard.
// Look & feel comes from the plugin's settings (Settings → Overlay look) and updates live; query params override:
// ?token=  &size=40  &names=0  &accent=%23ff8a00  &spread=34 (min px between cars, 0 = raw positions)
// The bar is centred in its own window: size the OBS source to the bar itself, then place it. ?offsetY= nudges it.
// ?spreadMode=field (leader paces the bar, the field spread out behind) or track (raw progress along the route).
import { query } from "./lib/query.js";
import { OVERLAY_DEFAULTS } from "./lib/defaults.js";
import { escapeHtml, ordinal } from "./lib/text.js";
import { createFeed, avatarHtml } from "./overlay-shared.js";

const BANNER_MS = 3000;
const BOOM_FUSE_MS = 5000; // the in-game fuse before a boomed car blows

let look = { ...OVERLAY_DEFAULTS };
function applyLook(saved) {
  look = { ...saved };
  if (query.get("size")) look.size = +query.get("size");
  if (query.get("spread") != null) look.spread = +query.get("spread");
  if (query.get("spreadMode")) look.spreadMode = query.get("spreadMode");
  if (query.get("offsetY") != null) look.offsetY = +query.get("offsetY");
  if (query.get("names") === "0") look.names = false;
  if (query.get("accent")) look.accent = query.get("accent");
  if (query.get("nameColors") != null) look.nameColors = query.get("nameColors") === "1";
  const rootStyle = document.documentElement.style;
  rootStyle.setProperty("--size", look.size + "px"); rootStyle.setProperty("--hazard", look.accent); rootStyle.setProperty("--line", look.line);
  rootStyle.setProperty("--line-h", look.lineHeight + "px"); rootStyle.setProperty("--nudge", (look.offsetY ?? 0) + "px"); rootStyle.setProperty("--side", look.side + "px");
  document.body.classList.toggle("no-banner", !look.banner);
  document.body.classList.toggle("no-names", !look.names);
  document.body.classList.toggle("plain-names", look.nameColors === false);
  render();
}

const racersElement = document.getElementById("racers");
const trackElement = document.getElementById("track");
const bannerElement = document.getElementById("banner");
const feed = createFeed();

// Smooth movement: each car keeps a shown progress that chases its latest reported one.
const shownProgress = new Map(); // login -> {current, target}
feed.on("pos", (frame) => {
  for (const [login, , , pct] of frame.v) {
    const shown = shownProgress.get(login);
    if (shown) shown.target = pct; else shownProgress.set(login, { current: pct, target: pct });
  }
});
feed.on("snapshot", (snapshot) => { if (!snapshot.vehicles.length) shownProgress.clear(); render(); });
feed.on("phase", render);
feed.on("settings", applyLook);
let lastFrameAt = performance.now();
function animate(now) {
  const blend = 1 - Math.exp(-(now - lastFrameAt) / 60); lastFrameAt = now; // ~60 ms ease
  for (const [, shown] of shownProgress) shown.current += (shown.target - shown.current) * blend;
  render(); requestAnimationFrame(animate);
}
requestAnimationFrame(animate);
const effects = new Map(); // login -> {className, until}

function render() {
  const vehicles = feed.vehicles();
  const count = vehicles.length;
  const seen = new Set();
  // Where each car sits on the bar.
  //   "field" (default): the leader paces the bar. They sit at the front and everyone else is placed by how far
  //     behind the leader they are, stretched across the whole width, so a tight pack is still readable.
  //   "track": the raw progress along the track, which bunches everyone together late in a race.
  // Either way a minimum gap keeps them from stacking: spread px between neighbours, -1 = 85% of the avatar size.
  const width = racersElement.clientWidth || trackElement.clientWidth || 0;
  const gap = (look.spread ?? -1) < 0 ? look.size * 0.85 : look.spread;
  const byPlace = [...vehicles].sort((a, b) => a.place - b.place);
  const shownPercent = (racer) => { const shown = shownProgress.get(racer.login); return racer.finished ? 100 : shown ? shown.current : racer.pct; };
  const leaderPercent = byPlace.length ? shownPercent(byPlace[0]) : 0;
  const lastPercent = byPlace.length ? shownPercent(byPlace[byPlace.length - 1]) : 0;
  const behind = Math.max(0.5, leaderPercent - lastPercent);   // never divide by a dead heat
  const fraction = (racer) => (look.spreadMode === "track" ? shownPercent(racer) / 100 : 1 - (leaderPercent - shownPercent(racer)) / behind);
  const leftByLogin = new Map();
  let previousLeft = Infinity;
  for (const racer of byPlace) {
    const rawLeft = Math.max(0, Math.min(1, fraction(racer))) * width;
    const left = gap > 0 && width > 0 ? Math.max(0, Math.min(rawLeft, previousLeft - gap)) : rawLeft;
    leftByLogin.set(racer.login, left); previousLeft = left;
  }
  for (const racer of vehicles) {
    seen.add(racer.login);
    let element = racersElement.querySelector(`[data-login="${CSS.escape(racer.login)}"]`);
    if (!element) {
      element = document.createElement("div"); element.dataset.login = racer.login;
      element.innerHTML = `${avatarHtml(racer)}<span class="place"></span><div class="name">${escapeHtml(racer.displayName)}</div>`;
      racersElement.appendChild(element);
    }
    const effect = effects.get(racer.login); const effectClass = effect && effect.until > Date.now() ? " " + effect.className : "";
    element.className = `racer p${racer.place}${racer.finished ? " finished" : ""}${effectClass}`;
    element.style.setProperty("--c", racer.color || "#fff");
    element.style.left = (width > 0 ? leftByLogin.get(racer.login) + "px" : (racer.finished ? 100 : racer.pct) + "%");
    element.style.zIndex = count - racer.place + 1;
    element.querySelector(".place").textContent = racer.place;
  }
  for (const element of [...racersElement.children]) if (!seen.has(element.dataset.login)) element.remove();
}

function showEffect(login, className, ms) { effects.set(login, { className, until: Date.now() + ms }); render(); setTimeout(render, ms + 20); }
let bannerTimer;
function showBanner(markup, className = "") {
  bannerElement.innerHTML = markup; bannerElement.className = "show " + className;
  clearTimeout(bannerTimer); bannerTimer = setTimeout(() => (bannerElement.className = ""), BANNER_MS);
}
feed.on("boom", (racer) => { showBanner(`RIP <b>${escapeHtml(racer.displayName)}</b>`); setTimeout(() => showEffect(racer.login, "boom", 900), BOOM_FUSE_MS); });
feed.on("boost", (racer) => showEffect(racer.login, "boosting", 1800));
feed.on("respawn", (racer) => showEffect(racer.login, "respawn", 2000));
feed.on("finisher", (racer) => showBanner(`<b>${escapeHtml(racer.displayName)}</b> finished ${ordinal(racer.place)}`, "fin"));

applyLook(look);
feed.start();
