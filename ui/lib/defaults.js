// Defaults of the look settings that both the Settings page and the browser sources need, so the form and the
// page it configures never disagree: settings.overlay (/overlay + /leaderboard) and settings.minimap (/minimap).
export const OVERLAY_DEFAULTS = {
  size: 40, names: true, spread: -1, accent: "#ffd400", line: "rgba(255,255,255,.35)", lineHeight: 6, offsetY: 0, side: 24, banner: true, nameColors: true, showInLobby: false,
  board: 10, boardSide: "left", boardScale: 1, // leaderboard: rows, anchor, size
};

// x / y / w are fractions of the game window from the bottom-left; h is unused (the aspect shapes the box).
export const MINIMAP_DEFAULTS = { enabled: true, x: 0.02, y: 0.03, w: 0.18, h: 0, marker: 3, bg: "#000000", alpha: 0, track: "#ffffff", pad: 1.15, leaderBig: true, names: true, mapTitle: true, showInLobby: false, aspect: "1:1" };
export const MINIMAP_RATIOS = ["1:1", "16:9", "4:3", "21:9", "3:2", "9:16", "auto"];
