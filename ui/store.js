import { create } from "zustand";
import htm from "htm";
import { createElement } from "react";

export const html = htm.bind(createElement);

// Load a stylesheet once, next to the module that owns it.
const loaded = new Set();
export function useCss(href) {
  if (loaded.has(href)) return;
  loaded.add(href);
  const l = document.createElement("link");
  l.rel = "stylesheet"; l.href = href;
  document.head.appendChild(l);
}

export function toast(msg, bad) {
  let t = document.getElementById("toast");
  if (!t) { t = document.createElement("div"); t.id = "toast"; document.body.appendChild(t); }
  t.textContent = msg; t.className = "show" + (bad ? " bad" : "");
  clearTimeout(t._h); t._h = setTimeout(() => (t.className = ""), 1600);
}

export const getToken = () => { try { return localStorage.getItem("sr.token") || ""; } catch { return ""; } };
export const setToken = (t) => { try { localStorage.setItem("sr.token", t || ""); } catch {} };
export const authHeaders = () => (getToken() ? { Authorization: "Bearer " + getToken() } : {});
export const withToken = (path) => (getToken() ? path + (path.includes("?") ? "&" : "?") + "token=" + encodeURIComponent(getToken()) : path);

export async function api(path, { method = "POST", body } = {}) {
  try {
    const r = await fetch(path, { method, headers: { "Content-Type": "application/json", ...authHeaders() }, body: body === undefined ? (method === "GET" ? undefined : "") : JSON.stringify(body) });
    if (r.status === 401) { toast("api token required — see Settings", true); return null; }
    const j = await r.json().catch(() => ({}));
    if (method !== "GET") toast(r.ok ? `${path.split("?")[0]} · ${j.affected ?? "ok"}` : `${j.error || r.status}`, !r.ok);
    return r.ok ? j : null;
  } catch { toast("game not running", true); return null; }
}

let es, retryTimer;
if (location.hash.startsWith("#/")) history.replaceState(null, "", "/" + location.hash.slice(2)); // old #/page links

const DEFAULT_UI = { boomCount: 1, addBoosts: 1, slowMult: 0.3, slowSecs: 5, boostForce: "", boostSecs: "", cameraOnControls: false };

export const useStore = create((set, get) => ({
  page: location.pathname.replace(/^\//, "") || "controls",
  snap: { running: false, streamer: null, vehicles: [] },
  me: {},
  version: {},
  loadVersion: async () => { const v = await api("/version", { method: "GET" }); if (v) set({ version: v }); },
  settings: { autoJoin: [], ui: DEFAULT_UI, config: {} },
  online: false,
  search: "",
  camera: { auto: false, mode: null, target: null },

  setPage: (page) => { history.pushState(null, "", "/" + page); set({ page }); },
  setSearch: (search) => set({ search }),
  ui: () => ({ ...DEFAULT_UI, ...get().settings.ui }),

  refresh: async () => { const snap = await api("/race", { method: "GET" }); if (snap) set({ snap, online: true }); const cam = await api("/camera", { method: "GET" }); if (cam) set({ camera: cam }); },
  loadMe: async () => { const me = await api("/me", { method: "GET" }); if (me) set({ me }); },
  loadSettings: async () => { const s = await api("/settings", { method: "GET" }); if (s) set({ settings: { ...s, ui: { ...DEFAULT_UI, ...s.ui } } }); },
  saveSettings: async (patch) => {
    const next = { ...get().settings, ...patch };
    const s = await api("/settings", { method: "PUT", body: { autoJoinStreamer: next.autoJoinStreamer, streamerColor: next.streamerColor, autoJoin: next.autoJoin, ui: next.ui, bots: next.bots, customBots: next.customBots, overlay: next.overlay, minimap: next.minimap, camera: next.camera, colorLeaderboard: next.colorLeaderboard, colorCommandEnabled: next.colorCommandEnabled, colorCommand: next.colorCommand, respawnCommandEnabled: next.respawnCommandEnabled, respawnLimit: next.respawnLimit, respawnCommand: next.respawnCommand, colors: next.colors, perks: next.perks, twitchToken: next.twitchToken, twitchClientId: next.twitchClientId, botOptions: next.botOptions, webhooks: next.webhooks } });
    if (s) set({ settings: { ...s, ui: { ...DEFAULT_UI, ...s.ui } } });
  },

  connect: () => {
    clearTimeout(retryTimer); es?.close();
    es = new EventSource(withToken("/events"));
    for (const ev of ["lobby", "race_start", "positions", "race_end"])
      es.addEventListener(ev, (e) => set({ snap: JSON.parse(e.data), online: true }));
    es.addEventListener("joined", get().refresh);
    es.addEventListener("finisher", get().refresh);
    // Boost pools change between the 4 Hz snapshots (a !boost, a perk, /boost/:x/add): patch the count straight away.
    // The next `positions` snapshot replaces the whole field and stays the truth.
    const patchBoosts = (e) => {
      const { login, boosts } = JSON.parse(e.data);
      if (typeof boosts !== "number" || !login) return;
      set((state) => ({
        snap: { ...state.snap, vehicles: state.snap.vehicles.map((v) => (v.login === login ? { ...v, boosts } : v)) },
        me: login === state.snap.streamer ? { ...state.me, boosts } : state.me,
      }));
    };
    es.addEventListener("boost", patchBoosts);
    es.addEventListener("boosts", patchBoosts);
    es.addEventListener("camera", (e) => set({ camera: JSON.parse(e.data) }));
    es.addEventListener("screen", () => { get().refresh(); get().loadMe(); });
    es.addEventListener("settings", (e) => { const s = JSON.parse(e.data); set({ settings: { ...s, ui: { ...DEFAULT_UI, ...s.ui } } }); });
    es.onopen = () => { set({ online: true }); get().refresh(); get().loadMe(); get().loadSettings(); get().loadVersion(); };
    // Chromium quietly stops retrying after a few refused connections; a game restart takes longer than that.
    es.onerror = () => { set({ online: false }); es.close(); retryTimer = setTimeout(get().connect, 3000); };
  },
}));

window.addEventListener("popstate", () => useStore.setState({ page: location.pathname.replace(/^\//, "") || "controls" }));
