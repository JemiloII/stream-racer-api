// What /overlay (horizontal bar) and /leaderboard (vertical top-N) share: the event stream with our own reconnect,
// the race snapshot kept fresh by the 60 Hz `pos` frames, the look settings (Settings → Overlay look, live), and the
// race phase that decides whether anything is drawn at all:
//   racing            → drawn
//   ended (race_end)  → cars fade out over ~1 s, then nothing until the next lobby / race_start
//   lobby             → nothing unless settings.overlay.showInLobby is on
//   idle / post game  → nothing
import { tokenQuery } from "./lib/query.js";
import { OVERLAY_DEFAULTS } from "./lib/defaults.js";
import { escapeHtml, initialsOf } from "./lib/text.js";

export const FADE_MS = 1000; // how long the cars take to fade after race_end

// A racer's picture for innerHTML: the avatar, or initials in a colored box.
// Custom bots carry `image` (a local file the mod serves at /image/<login>); Twitch racers carry `avatar`.
export const avatarUrlOf = (racer) => (racer.image ? `/image/${encodeURIComponent(racer.login)}` : racer.avatar || "");
export const avatarHtml = (racer, className = "") => {
  const url = avatarUrlOf(racer);
  return url ? `<img class="pic ${className}" src="${escapeHtml(url)}" alt="" onerror="this.replaceWith(Object.assign(document.createElement('div'),{className:'pic init ${className}',textContent:'${escapeHtml(initialsOf(racer))}'}))">`
             : `<div class="pic init ${className}">${escapeHtml(initialsOf(racer))}</div>`;
};

const byPlace = (a, b) => a.place - b.place;

// A tiny feed object: `feed.on(name, listener)` for "snapshot" (any change to the field), "pos" (one 60 Hz frame),
// "settings" (settings.overlay with defaults), "phase", and the pass-through game events (boom, boost, respawn, finisher).
export function createFeed() {
  const listeners = {};
  const emit = (name, ...args) => { for (const listener of listeners[name] || []) listener(...args); };
  let fadeTimer;

  const feed = {
    snapshot: { running: false, lobby: false, vehicles: [] },
    phase: "idle",
    look: { ...OVERLAY_DEFAULTS },
    on(name, listener) { (listeners[name] ||= []).push(listener); return feed; },
    visible() { return feed.phase === "racing" || feed.phase === "ended" || (feed.phase === "lobby" && !!feed.look.showInLobby); },
    vehicles() { return feed.visible() ? feed.snapshot.vehicles : []; },
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
      fadeTimer = setTimeout(() => { feed.snapshot = { ...feed.snapshot, running: false, vehicles: [] }; feed.phase = "idle"; reflect(); emit("phase", "idle"); emit("snapshot", feed.snapshot); }, FADE_MS + 100);
    }
    reflect(); emit("phase", phase);
  }
  function phaseOf(snapshot) { return snapshot.running ? "racing" : snapshot.lobby ? "lobby" : "idle"; }
  function setSnapshot(snapshot, phase) {
    feed.snapshot = snapshot || { vehicles: [] };
    feed.snapshot.vehicles = (feed.snapshot.vehicles || []).slice().sort(byPlace);
    // A fetch landing mid-fade must not cut the fade short; the fade timer ends it.
    if (!(feed.phase === "ended" && phase === undefined)) setPhase(phase ?? phaseOf(feed.snapshot));
    emit("snapshot", feed.snapshot);
  }
  function applySettings(overlay) {
    feed.look = { ...OVERLAY_DEFAULTS, ...(overlay || {}) };
    reflect(); emit("settings", feed.look);
  }
  function applyPos(frame) {
    const vehiclesByLogin = new Map(feed.snapshot.vehicles.map((vehicle) => [vehicle.login, vehicle]));
    for (const [login, x, z, pct, place, finished] of frame.v || []) {
      const vehicle = vehiclesByLogin.get(login); if (!vehicle) continue;
      vehicle.x = x; vehicle.z = z; vehicle.pct = pct; vehicle.place = place; vehicle.finished = !!finished;
    }
    feed.snapshot.vehicles.sort(byPlace);
    emit("pos", frame);
  }

  const fetchRace = () => fetch("/race" + tokenQuery).then((response) => response.json()).then((snapshot) => setSnapshot(snapshot)).catch(() => {});
  function resync() {
    fetchRace();
    fetch("/settings" + tokenQuery).then((response) => response.json()).then((settings) => applySettings(settings.overlay)).catch(() => {});
  }
  let eventSource, retryTimer;
  function connect() {
    clearTimeout(retryTimer); eventSource?.close();
    eventSource = new EventSource("/events" + tokenQuery);
    const payloadOf = (event) => JSON.parse(event.data);
    eventSource.addEventListener("lobby", (event) => setSnapshot(payloadOf(event), "lobby"));
    eventSource.addEventListener("race_start", (event) => setSnapshot(payloadOf(event), "racing"));
    eventSource.addEventListener("positions", (event) => setSnapshot(payloadOf(event), "racing"));
    eventSource.addEventListener("race_end", (event) => setSnapshot(payloadOf(event), "ended"));
    eventSource.addEventListener("pos", (event) => applyPos(payloadOf(event)));
    eventSource.addEventListener("joined", fetchRace);
    eventSource.addEventListener("screen", (event) => { const screen = payloadOf(event); if (feed.phase !== "ended" && !screen.running && !screen.lobby) setPhase("idle"); });
    eventSource.addEventListener("settings", (event) => applySettings(payloadOf(event).overlay));
    for (const name of ["boom", "boost", "respawn", "finisher"]) eventSource.addEventListener(name, (event) => emit(name, payloadOf(event)));
    // Chromium quietly stops retrying after a few refused connections (a game restart takes ~35 s),
    // so we never rely on its retry: every error closes the stream and we reopen it ourselves, forever.
    eventSource.onopen = () => { console.log("[overlay] events connected"); resync(); };
    eventSource.onerror = () => { console.log("[overlay] events lost, retrying in 3s"); eventSource.close(); retryTimer = setTimeout(connect, 3000); };
  }
  return feed;
}
