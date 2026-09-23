// Browser-source mini map: the route outline + car dots, drawn on a canvas that fills the page.
// Same look settings as the in-game map (Settings → Mini map), live via the settings event.
// Hidden outside a race: the map fades out on race_end and stays gone until the next lobby / race (?showInLobby=1
// draws it during the lobby too).
// Query: ?token=  &mapTitle=0 (hide the map name + author above the box)
// Query: ?token=  &names=1 (labels)  &leaderBig=1  &aspect=16:9  &track=%23fff  &bg=%23000  &alpha=0  &marker=3
import { query, tokenQuery } from "./lib/query.js";
import { MINIMAP_DEFAULTS } from "./lib/defaults.js";

// The canvas fills the window; the map box is the largest rectangle of the chosen aspect that fits, centred.
// Size the OBS source to that ratio and the box fills it exactly.
let boxWidth = 400, boxHeight = 300, boxLeft = 0, boxTop = 0;
let look = { ...MINIMAP_DEFAULTS };

const canvas = document.getElementById("map");
const context = canvas.getContext("2d");
let snapshot = { vehicles: [] }, route = [], bounds = null;
// Smooth movement: each car keeps a shown position that chases its latest reported one.
const shownPositions = new Map(); // login -> {x, z, targetX, targetZ}
const rememberPosition = (vehicle) => { if (vehicle.x != null && !shownPositions.has(vehicle.login)) shownPositions.set(vehicle.login, { x: vehicle.x, z: vehicle.z, targetX: vehicle.x, targetZ: vehicle.z }); };
function applyPos(frame) {
  const vehiclesByLogin = new Map(snapshot.vehicles.map((vehicle) => [vehicle.login, vehicle]));
  for (const [login, x, z, pct, place, finished] of frame.v) {
    const vehicle = vehiclesByLogin.get(login); if (!vehicle) continue;
    vehicle.x = x; vehicle.z = z; vehicle.pct = pct; vehicle.place = place; vehicle.finished = !!finished;
    const shown = shownPositions.get(login); if (shown) { shown.targetX = x; shown.targetZ = z; } else shownPositions.set(login, { x, z, targetX: x, targetZ: z });
  }
  snapshot.vehicles.sort((a, b) => a.place - b.place);
}
let lastFrameAt = performance.now();
function animate(now) {
  const blend = 1 - Math.exp(-(now - lastFrameAt) / 60); lastFrameAt = now; // ~60 ms ease
  for (const [, shown] of shownPositions) { shown.x += (shown.targetX - shown.x) * blend; shown.z += (shown.targetZ - shown.z) * blend; }
  draw(); requestAnimationFrame(animate);
}
requestAnimationFrame(animate);

function applyLook(saved) {
  look = { ...MINIMAP_DEFAULTS, ...(saved || {}) };
  for (const key of ["track", "bg", "alpha", "marker", "pad"]) if (query.get(key)) look[key] = key === "track" || key === "bg" ? query.get(key) : +query.get(key);
  if (query.get("names") != null) look.names = query.get("names") === "1";
  if (query.get("showInLobby") != null) look.showInLobby = query.get("showInLobby") === "1";
  if (query.get("mapTitle") != null) look.mapTitle = query.get("mapTitle") === "1";
  if (query.get("leaderBig") != null) look.leaderBig = query.get("leaderBig") === "1";
  if (query.get("aspect")) look.aspect = query.get("aspect");
  fit();
}

// Width ÷ height of the map box: "16:9", "4x3", or "auto" = the track's own bounds.
function aspectRatio() {
  const aspect = String(look.aspect || "16:9").trim().toLowerCase();
  if (aspect === "auto" && bounds) return Math.max(0.2, (bounds.maxX - bounds.minX) / Math.max(1, bounds.maxZ - bounds.minZ));
  const match = /^(\d+(?:\.\d+)?)\s*[:x\/]\s*(\d+(?:\.\d+)?)$/.exec(aspect);
  return match ? +match[1] / +match[2] : 16 / 9;
}
// Room kept above the box for the map name and its author.
const titleHeight = () => (look.mapTitle === false ? 0 : Math.max(34, Math.min(72, innerHeight * 0.13)));   // two lines: name, then the author

function fit() {
  const dpr = devicePixelRatio || 1;
  canvas.style.width = innerWidth + "px"; canvas.style.height = innerHeight + "px";
  canvas.width = innerWidth * dpr; canvas.height = innerHeight * dpr;
  const ratio = aspectRatio(), title = titleHeight();
  boxWidth = innerWidth; boxHeight = boxWidth / ratio;
  if (boxHeight > innerHeight - title) { boxHeight = innerHeight - title; boxWidth = boxHeight * ratio; }
  boxLeft = (innerWidth - boxWidth) / 2; boxTop = title + (innerHeight - title - boxHeight) / 2;
  context.setTransform(dpr, 0, 0, dpr, boxLeft * dpr, boxTop * dpr);
  draw();
}
addEventListener("resize", fit);

// World (x, z) → box pixels, the track centred and scaled to fit with `pad` margin. Null until the track is known.
function projector() {
  if (!bounds) return null;
  const spanX = Math.max(1, bounds.maxX - bounds.minX) * look.pad, spanZ = Math.max(1, bounds.maxZ - bounds.minZ) * look.pad;
  // names sit to the right of their dot and never move: reserve room for ~12 characters on the right of the box
  const labelRoomPx = look.names ? 12 * Math.max(9, Math.min(boxWidth, boxHeight) * look.marker / 100 * 0.9) * 0.55 : 0;
  const scale = Math.min((boxWidth - labelRoomPx) / spanX, boxHeight / spanZ);
  const centerX = (bounds.minX + bounds.maxX) / 2, centerZ = (bounds.minZ + bounds.maxZ) / 2;
  return (x, z) => [(boxWidth - labelRoomPx) / 2 + (x - centerX) * scale, boxHeight / 2 - (z - centerZ) * scale]; // z up on screen, like looking down with north up
}

function hexToRgba(hex, alpha) {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex || "");
  if (!match) return `rgba(0,0,0,${alpha})`;
  const rgb = parseInt(match[1], 16);
  return `rgba(${rgb >> 16 & 255},${rgb >> 8 & 255},${rgb & 255},${alpha})`;
}

// Which phase the race is in decides whether the map is drawn at all: racing (and the lobby when showInLobby is on)
// draw; race_end fades the whole map out over a second; after that, and on any other screen, nothing is drawn.
const FADE_MS = 1000;
let phase = "idle", fadeTimer;
const visible = () => phase === "racing" || phase === "ended" || (phase === "lobby" && !!look.showInLobby);
function setPhase(next) {
  if (next === phase) return;
  clearTimeout(fadeTimer);
  phase = next;
  canvas.style.transition = next === "ended" ? `opacity ${FADE_MS}ms ease-out` : "none";
  canvas.style.opacity = next === "ended" ? "0" : visible() ? "1" : "0";
  if (next === "ended") fadeTimer = setTimeout(() => { snapshot = { vehicles: [] }; shownPositions.clear(); setPhase("idle"); draw(); }, FADE_MS + 100);
  draw();
}
const phaseOf = (race) => (race?.running ? "racing" : race?.lobby ? "lobby" : "idle");

function draw() {
  context.clearRect(-boxLeft, -boxTop, innerWidth, innerHeight);
  if (!visible()) return;
  context.fillStyle = hexToRgba(look.bg, look.alpha);
  roundRect(0, 0, boxWidth, boxHeight, 10); context.fill();
  drawMapTitle();
  const toScreen = projector();
  if (!toScreen || route.length < 2) return;
  context.lineCap = "round"; context.lineJoin = "round";
  context.strokeStyle = look.track; context.lineWidth = Math.max(2, boxHeight * 0.018);
  context.beginPath();
  route.forEach(([x, z], index) => { const [px, py] = toScreen(x, z); index ? context.lineTo(px, py) : context.moveTo(px, py); });
  context.stroke();
  const leader = snapshot.vehicles.find((vehicle) => !vehicle.finished) || snapshot.vehicles[0];
  const dotSize = boxHeight * look.marker / 100;
  const dots = [];
  for (const vehicle of [...snapshot.vehicles].reverse()) { // draw 1st last so it sits on top
    if (vehicle.x == null) continue;
    const shown = shownPositions.get(vehicle.login);
    const [px, py] = toScreen(shown ? shown.x : vehicle.x, shown ? shown.z : vehicle.z);
    const radius = dotSize / 2 * (look.leaderBig && vehicle === leader ? 1.6 : 1) * (vehicle.finished ? 0.6 : 1);
    context.beginPath(); context.arc(px, py, radius, 0, Math.PI * 2);
    context.fillStyle = vehicle.color || "#fff"; context.fill();
    context.lineWidth = Math.max(1, radius * 0.25); context.strokeStyle = "rgba(0,0,0,.7)"; context.stroke();
    dots.push({ vehicle, px, py, radius });
  }
  if (look.names) drawLabels(dots, dotSize);
}

// "Locate Yourself · by MindZoneRL" above the box, in the accent colour.
function drawMapTitle() {
  const map = snapshot?.map; const title = titleHeight();
  if (look.mapTitle === false || !map?.name || !title) return;
  const nameSize = Math.max(15, title * 0.46), authorSize = nameSize * 0.72;
  context.textAlign = "center"; context.textBaseline = "alphabetic";
  context.lineWidth = 4; context.strokeStyle = "rgba(0,0,0,.9)"; context.lineJoin = "round";
  context.fillStyle = look.track || "#fff";
  const draw = (text, size, y) => {
    context.font = `700 ${size}px "Chakra Petch", sans-serif`;
    context.strokeText(text, boxWidth / 2, y); context.fillText(text, boxWidth / 2, y);
  };
  if (map.creator) { draw(map.name, nameSize, -title * 0.5); draw(`by ${map.creator}`, authorSize, -title * 0.12); }
  else draw(map.name, nameSize, -title * 0.3);
  context.textAlign = "left";
}

// Names to the right of their dot, and they stay there: no flipping, no pushing apart (the projector leaves room).
function drawLabels(dots, dotSize) {
  const fontSize = Math.max(9, dotSize * 0.9), gap = dotSize * 0.7;
  context.font = `600 ${fontSize}px "Chakra Petch", sans-serif`; context.textBaseline = "middle"; context.textAlign = "left";
  for (const dot of dots) {
    const text = dot.vehicle.displayName || dot.vehicle.login;
    context.lineWidth = 3; context.strokeStyle = "rgba(0,0,0,.85)"; context.lineJoin = "round"; context.strokeText(text, dot.px + gap, dot.py);
    context.fillStyle = dot.vehicle.color || "#fff"; context.fillText(text, dot.px + gap, dot.py);
  }
  context.textBaseline = "alphabetic";
}
function roundRect(x, y, width, height, radius) {
  context.beginPath(); context.moveTo(x + radius, y);
  context.arcTo(x + width, y, x + width, y + height, radius); context.arcTo(x + width, y + height, x, y + height, radius);
  context.arcTo(x, y + height, x, y, radius); context.arcTo(x, y, x + width, y, radius); context.closePath();
}

async function loadTrack() {
  try { const track = await (await fetch("/track" + tokenQuery)).json(); route = track.points || []; bounds = track.bounds || null; } catch { route = []; bounds = null; }
  fit();
}
const fetchRace = () => fetch("/race" + tokenQuery).then((response) => response.json()).then((race) => { snapshot = race; if (phase !== "ended") setPhase(phaseOf(race)); }).catch(() => {});
function resync() {
  fetchRace();
  fetch("/settings" + tokenQuery).then((response) => response.json()).then((settings) => applyLook(settings.minimap)).catch(() => {});
  loadTrack();
}

let eventSource, retryTimer;
function connect() {
  clearTimeout(retryTimer); eventSource?.close();
  eventSource = new EventSource("/events" + tokenQuery);
  const onSnapshot = (event) => { snapshot = JSON.parse(event.data); for (const vehicle of snapshot.vehicles) rememberPosition(vehicle); if (phase !== "ended") setPhase(phaseOf(snapshot)); };
  for (const name of ["lobby", "race_start", "positions"]) eventSource.addEventListener(name, onSnapshot);
  // race over: fade the whole map out, then draw nothing until the next lobby or race
  eventSource.addEventListener("race_end", () => setPhase("ended"));
  eventSource.addEventListener("screen", (event) => { const screen = JSON.parse(event.data); if (phase !== "ended" && !screen.running && !screen.lobby) setPhase("idle"); });
  eventSource.addEventListener("lobby", () => { shownPositions.clear(); setPhase("lobby"); });
  eventSource.addEventListener("race_start", () => setPhase("racing"));
  eventSource.addEventListener("pos", (event) => applyPos(JSON.parse(event.data)));
  eventSource.addEventListener("race_start", loadTrack);
  eventSource.addEventListener("lobby", loadTrack);
  eventSource.addEventListener("joined", () => fetch("/race" + tokenQuery).then((response) => response.json()).then((race) => { snapshot = race; }));
  eventSource.addEventListener("settings", (event) => applyLook(JSON.parse(event.data).minimap));
  eventSource.onopen = resync;
  eventSource.onerror = () => { eventSource.close(); retryTimer = setTimeout(connect, 3000); };
}
applyLook({}); resync(); connect();
