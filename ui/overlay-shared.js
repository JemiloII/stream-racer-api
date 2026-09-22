// What /overlay (horizontal bar) and /leaderboard (vertical top-N) share: the event stream with our own reconnect,
// the race snapshot kept fresh by the 60 Hz `pos` frames, the look settings (Settings → Overlay look, live), and the
// race phase that decides whether anything is drawn at all:
//   racing            → drawn
//   ended (race_end)  → cars fade out over ~1 s, then nothing until the next lobby / race_start
//   lobby             → nothing unless settings.overlay.showInLobby is on
//   idle / post game  → nothing
export const query = new URLSearchParams(location.search);
export const token = query.get("token") || "";
export const tokenQuery = token ? "?token=" + encodeURIComponent(token) : "";

export const DEFAULTS = {
  size: 40, names: true, accent: "#ffd400", line: "rgba(255,255,255,.35)", lineHeight: 6, bottom: 28, side: 24, banner: true, showInLobby: false,
  board: 10, boardSide: "left", boardScale: 1, // leaderboard: rows, anchor, size
};
export const FADE_MS = 1000; // how long the cars take to fade after race_end

export const esc = (text) => String(text ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
export const initials = (racer) => esc((racer.displayName || racer.login || "?").slice(0, 2).toUpperCase());
export const pic = (racer, cls = "") => racer.avatar ? `<img class="pic ${cls}" src="${esc(racer.avatar)}" alt="">` : `<div class="pic init ${cls}">${initials(racer)}</div>`;
export const ordinal = (place) => place === 1 ? "1st" : place === 2 ? "2nd" : place === 3 ? "3rd" : place + "th";

// A tiny feed object: `feed.on(name, fn)` for "snapshot" (any change to the field), "pos" (one 60 Hz frame),
// "settings" (settings.overlay), "phase", and the pass-through game events (boom, boost, respawn, finisher).
export function createFeed() {
  const listeners = {};
  const emit = (name, ...args) => { for (const fn of listeners[name] || []) fn(...args); };
  let fadeTimer;

  const feed = {
    snap: { running: false, lobby: false, vehicles: [] },
    phase: "idle",
    cfg: { ...DEFAULTS },
    on(name, fn) { (listeners[name] ||= []).push(fn); return feed; },
    visible() { return feed.phase === "racing" || feed.phase === "ended" || (feed.phase === "lobby" && !!feed.cfg.showInLobby); },
    vehicles() { return feed.visible() ? feed.snap.vehicles : []; },
    start() { resync(); connect(); return feed; },
    // Fake-able for tests: everything below goes through these.
    setSnapshot, setPhase, applySettings, applyPos,
  };

  function reflect() {
    document.body.classList.toggle("no-race", !feed.visible());
    document.body.classList.toggle("race-over", feed.phase === "ended");
  }
  function setPhase(phase) {
    if (phase === feed.phase) { reflect(); return; }
    clearTimeout(fadeTimer);
    feed.phase = phase;
    if (phase === "ended") {
      // Cars fade for FADE_MS (CSS transition on body.race-over), then the field is dropped so nothing is left drawn.
      fadeTimer = setTimeout(() => { feed.snap = { ...feed.snap, running: false, vehicles: [] }; feed.phase = "idle"; reflect(); emit("phase", "idle"); emit("snapshot", feed.snap); }, FADE_MS + 100);
    }
    reflect(); emit("phase", phase);
  }
  function phaseOf(snapshot) { return snapshot.running ? "racing" : snapshot.lobby ? "lobby" : "idle"; }
  function setSnapshot(snapshot, phase) {
    feed.snap = snapshot || { vehicles: [] };
    feed.snap.vehicles = (feed.snap.vehicles || []).slice().sort((a, b) => a.place - b.place);
    // A fetch landing mid-fade must not cut the fade short; the fade timer ends it.
    if (!(feed.phase === "ended" && phase === undefined)) setPhase(phase ?? phaseOf(feed.snap));
    emit("snapshot", feed.snap);
  }
  function applySettings(overlay) {
    feed.cfg = { ...DEFAULTS, ...(overlay || {}) };
    reflect(); emit("settings", feed.cfg);
  }
  function applyPos(frame) {
    const byLogin = new Map(feed.snap.vehicles.map((v) => [v.login, v]));
    for (const [login, x, z, pct, place, finished] of frame.v || []) {
      const v = byLogin.get(login); if (!v) continue;
      v.x = x; v.z = z; v.pct = pct; v.place = place; v.finished = !!finished;
    }
    feed.snap.vehicles.sort((a, b) => a.place - b.place);
    emit("pos", frame);
  }

  function resync() {
    fetch("/race" + tokenQuery).then((r) => r.json()).then((s) => setSnapshot(s)).catch(() => {});
    fetch("/settings" + tokenQuery).then((r) => r.json()).then((s) => applySettings(s.overlay)).catch(() => {});
  }
  let es, retry;
  function connect() {
    clearTimeout(retry); es?.close();
    es = new EventSource("/events" + tokenQuery);
    es.addEventListener("lobby", (e) => setSnapshot(JSON.parse(e.data), "lobby"));
    es.addEventListener("race_start", (e) => setSnapshot(JSON.parse(e.data), "racing"));
    es.addEventListener("positions", (e) => setSnapshot(JSON.parse(e.data), "racing"));
    es.addEventListener("race_end", (e) => setSnapshot(JSON.parse(e.data), "ended"));
    es.addEventListener("pos", (e) => applyPos(JSON.parse(e.data)));
    es.addEventListener("joined", () => fetch("/race" + tokenQuery).then((r) => r.json()).then((s) => setSnapshot(s)).catch(() => {}));
    es.addEventListener("screen", (e) => { const s = JSON.parse(e.data); if (feed.phase !== "ended" && !s.running && !s.lobby) setPhase("idle"); });
    es.addEventListener("settings", (e) => applySettings(JSON.parse(e.data).overlay));
    for (const name of ["boom", "boost", "respawn", "finisher"]) es.addEventListener(name, (e) => emit(name, JSON.parse(e.data)));
    // Chromium quietly stops retrying after a few refused connections (a game restart takes ~35 s),
    // so we never rely on its retry: every error closes the stream and we reopen it ourselves, forever.
    es.onopen = () => { console.log("[overlay] events connected"); resync(); };
    es.onerror = () => { console.log("[overlay] events lost, retrying in 3s"); es.close(); retry = setTimeout(connect, 3000); };
  }
  return feed;
}
