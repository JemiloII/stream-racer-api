// Transparent OBS browser source, the vertical leaderboard: the top-N cars with place, avatar, name and progress.
// The horizontal bar is its own source, /overlay. Look & feel comes from Settings → Overlay look → Leaderboard
// (settings.overlay.board / boardSide / boardScale, live); query params override:
// ?token=  &rows=10 (0 = off)  &side=left|right  &scale=1  &accent=%23ff8a00
import { query } from "./lib/query.js";
import { OVERLAY_DEFAULTS } from "./lib/defaults.js";
import { escapeHtml } from "./lib/text.js";
import { createFeed, avatarHtml } from "./overlay-shared.js";

let look = { ...OVERLAY_DEFAULTS };
function applyLook(saved) {
  look = { ...saved };
  if (query.get("rows") != null) look.board = +query.get("rows");
  if (query.get("side")) look.boardSide = query.get("side");
  if (query.get("scale")) look.boardScale = +query.get("scale");
  if (query.get("accent")) look.accent = query.get("accent");
  const rootStyle = document.documentElement.style;
  rootStyle.setProperty("--hazard", look.accent); rootStyle.setProperty("--side", look.side + "px"); rootStyle.setProperty("--board-scale", look.boardScale);
  document.body.classList.toggle("board-right", look.boardSide === "right");
  render();
}

const boardElement = document.getElementById("board");
const feed = createFeed();
feed.on("snapshot", render).on("pos", render).on("phase", render).on("settings", applyLook);

function render() {
  const rows = look.board > 0 ? feed.vehicles().slice(0, look.board) : [];
  boardElement.innerHTML = rows.map((racer) => `
    <div class="row" style="--c:${escapeHtml(racer.color || "#fff")}"><span class="n">${racer.place}</span>${avatarHtml(racer)}
      <span class="nm">${escapeHtml(racer.displayName)}<small>${racer.finished ? "FIN" : (racer.pct || 0).toFixed(0) + "%"}</small></span></div>`).join("");
}

applyLook(look);
feed.start();
