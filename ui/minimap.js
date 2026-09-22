// Browser-source mini map: the route outline + car dots, drawn on a canvas that fills the page.
// Same look settings as the in-game map (Settings → Mini map), live via the settings event.
// Query: ?token=  &names=1 (labels)  &leaderBig=1  &aspect=16:9  &track=%23fff  &bg=%23000  &alpha=0.55  &marker=3
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
function fit() {
  const dpr = devicePixelRatio || 1;
  canvas.style.width = innerWidth + "px"; canvas.style.height = innerHeight + "px";
  canvas.width = innerWidth * dpr; canvas.height = innerHeight * dpr;
  const ratio = aspectRatio();
  boxWidth = innerWidth; boxHeight = boxWidth / ratio;
  if (boxHeight > innerHeight) { boxHeight = innerHeight; boxWidth = boxHeight * ratio; }
  boxLeft = (innerWidth - boxWidth) / 2; boxTop = (innerHeight - boxHeight) / 2;
  context.setTransform(dpr, 0, 0, dpr, boxLeft * dpr, boxTop * dpr);
  draw();
}
addEventListener("resize", fit);

// World (x, z) → box pixels, the track centred and scaled to fit with `pad` margin. Null until the track is known.
function projector() {
  if (!bounds) return null;
  const spanX = Math.max(1, bounds.maxX - bounds.minX) * look.pad, spanZ = Math.max(1, bounds.maxZ - bounds.minZ) * look.pad;
  const scale = Math.min(boxWidth / spanX, boxHeight / spanZ);
  const centerX = (bounds.minX + bounds.maxX) / 2, centerZ = (bounds.minZ + bounds.maxZ) / 2;
  return (x, z) => [boxWidth / 2 + (x - centerX) * scale, boxHeight / 2 - (z - centerZ) * scale]; // z up on screen, like looking down with north up
}

function hexToRgba(hex, alpha) {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex || "");
  if (!match) return `rgba(0,0,0,${alpha})`;
  const rgb = parseInt(match[1], 16);
  return `rgba(${rgb >> 16 & 255},${rgb >> 8 & 255},${rgb & 255},${alpha})`;
}

function draw() {
  context.clearRect(-boxLeft, -boxTop, innerWidth, innerHeight);
  context.fillStyle = hexToRgba(look.bg, look.alpha);
  roundRect(0, 0, boxWidth, boxHeight, 10); context.fill();
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

// Names beside their dot (right, or left near the edge), pushed apart vertically until nothing overlaps.
function drawLabels(dots, dotSize) {
  const fontSize = Math.max(9, dotSize * 0.9), lineHeight = fontSize * 1.15, gap = dotSize * 0.7;
  context.font = `600 ${fontSize}px "Chakra Petch", sans-serif`; context.textBaseline = "middle";
  const placed = [];
  for (const dot of [...dots].sort((a, b) => a.py - b.py)) {
    const text = dot.vehicle.displayName || dot.vehicle.login;
    const textWidth = context.measureText(text).width + 4;
    const flip = dot.px + gap + textWidth > boxWidth - 4;
    const left = flip ? dot.px - gap - textWidth : dot.px + gap, right = left + textWidth;
    let y = Math.min(Math.max(dot.py, fontSize * 0.6), boxHeight - fontSize * 0.6);
    let moved = true, guard = 0;
    while (moved && guard++ < 20) {
      moved = false;
      for (const other of placed) if (left < other.right && right > other.left && Math.abs(y - other.y) < lineHeight) { y = other.y + lineHeight; moved = true; }
    }
    placed.push({ left, right, y });
    context.textAlign = flip ? "right" : "left";
    const textX = flip ? dot.px - gap : dot.px + gap;
    context.lineWidth = 3; context.strokeStyle = "rgba(0,0,0,.85)"; context.lineJoin = "round"; context.strokeText(text, textX, y);
    context.fillStyle = dot.vehicle.color || "#fff"; context.fillText(text, textX, y);
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
const fetchRace = () => fetch("/race" + tokenQuery).then((response) => response.json()).then((race) => { snapshot = race; }).catch(() => {});
function resync() {
  fetchRace();
  fetch("/settings" + tokenQuery).then((response) => response.json()).then((settings) => applyLook(settings.minimap)).catch(() => {});
  loadTrack();
}

let eventSource, retryTimer;
function connect() {
  clearTimeout(retryTimer); eventSource?.close();
  eventSource = new EventSource("/events" + tokenQuery);
  const onSnapshot = (event) => { snapshot = JSON.parse(event.data); for (const vehicle of snapshot.vehicles) rememberPosition(vehicle); };
  for (const name of ["lobby", "race_start", "positions"]) eventSource.addEventListener(name, onSnapshot);
  // race over: clear the map (keep the outline)
  eventSource.addEventListener("race_end", () => { snapshot = { vehicles: [] }; shownPositions.clear(); });
  eventSource.addEventListener("lobby", () => { shownPositions.clear(); });
  eventSource.addEventListener("pos", (event) => applyPos(JSON.parse(event.data)));
  eventSource.addEventListener("race_start", loadTrack);
  eventSource.addEventListener("lobby", loadTrack);
  eventSource.addEventListener("joined", () => fetch("/race" + tokenQuery).then((response) => response.json()).then((race) => { snapshot = race; }));
  eventSource.addEventListener("settings", (event) => applyLook(JSON.parse(event.data).minimap));
  eventSource.onopen = resync;
  eventSource.onerror = () => { eventSource.close(); retryTimer = setTimeout(connect, 3000); };
}
applyLook({}); resync(); connect();
