// Transparent OBS browser source, the horizontal bar: racers as avatars along a track line, stacked by place,
// plus RIP / boost / finish effects. The vertical top-N list is its own source, /leaderboard.
// Look & feel comes from the plugin's settings (Settings → Overlay look) and updates live; query params override:
// ?token=  &size=40  &names=0  &accent=%23ff8a00  &spread=34 (min px between cars, 0 = raw positions)
// The bar is centred in its own window: size the OBS source to the bar itself, then place it. ?offsetY= nudges it.
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
  if (query.get("offsetY") != null) look.offsetY = +query.get("offsetY");
  if (query.get("names") === "0") look.names = false;
  if (query.get("accent")) look.accent = query.get("accent");
  const rootStyle = document.documentElement.style;
  rootStyle.setProperty("--size", look.size + "px"); rootStyle.setProperty("--hazard", look.accent); rootStyle.setProperty("--line", look.line);
  rootStyle.setProperty("--line-h", look.lineHeight + "px"); rootStyle.setProperty("--nudge", (look.offsetY ?? 0) + "px"); rootStyle.setProperty("--side", look.side + "px");
  document.body.classList.toggle("no-banner", !look.banner);
  document.body.classList.toggle("no-names", !look.names);
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
  // Min gap by place: cars keep their order but never stack on top of each other (the field bunches at the finish).
  // spread = px between neighbours, -1 = 85% of the avatar size, 0 = raw positions.
  const width = racersElement.clientWidth || trackElement.clientWidth || 0;
  const gap = (look.spread ?? -1) < 0 ? look.size * 0.85 : look.spread;
  const leftByLogin = new Map();
  let previousLeft = Infinity;
  for (const racer of [...vehicles].sort((a, b) => a.place - b.place)) {
    const shown = shownProgress.get(racer.login);
    const rawLeft = ((racer.finished ? 100 : (shown ? shown.current : racer.pct)) / 100) * width;
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
