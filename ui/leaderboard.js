// Transparent OBS browser source, the vertical leaderboard: the top-N cars with place, avatar, name and progress.
// The horizontal bar is its own source, /overlay. Look & feel comes from Settings → Overlay look → Leaderboard
// (settings.overlay.board / boardSide / boardScale, live); query params override:
// ?token=  &rows=10 (0 = off)  &side=left|right  &scale=1  &accent=%23ff8a00
import { createFeed, DEFAULTS, query, esc, pic } from "./overlay-shared.js";

let cfg = { ...DEFAULTS };
function applyCfg(saved) {
  cfg = { ...saved };
  if (query.get("rows") != null) cfg.board = +query.get("rows");
  if (query.get("side")) cfg.boardSide = query.get("side");
  if (query.get("scale")) cfg.boardScale = +query.get("scale");
  if (query.get("accent")) cfg.accent = query.get("accent");
  const root = document.documentElement.style;
  root.setProperty("--hazard", cfg.accent); root.setProperty("--side", cfg.side + "px"); root.setProperty("--board-scale", cfg.boardScale);
  document.body.classList.toggle("board-right", cfg.boardSide === "right");
  render();
}

const $board = document.getElementById("board");
const feed = createFeed();
feed.on("snapshot", render).on("pos", render).on("phase", render).on("settings", applyCfg);

function render() {
  const rows = cfg.board > 0 ? feed.vehicles().slice(0, cfg.board) : [];
  $board.innerHTML = rows.map((r) => `
    <div class="row" style="--c:${esc(r.color || "#fff")}"><span class="n">${r.place}</span>${pic(r)}
      <span class="nm">${esc(r.displayName)}<small>${r.finished ? "FIN" : (r.pct || 0).toFixed(0) + "%"}</small></span></div>`).join("");
}

applyCfg(cfg);
feed.start();
