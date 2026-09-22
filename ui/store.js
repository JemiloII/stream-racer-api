// The control page's state: one zustand store filled from GET /race, /camera, /me, /settings and /version and kept
// live by the /events stream (own reconnect). Pages read it with `useStore`; nothing in here renders.
import { create } from "zustand";
import { api, withToken } from "./lib/api.js";

/**
 * @typedef {object} Vehicle  One car in the field, as GET /race and the snapshot events describe it.
 * @property {number} place
 * @property {string} id            Twitch user id; "" for custom bots
 * @property {string} login
 * @property {string} displayName
 * @property {string} color         hex
 * @property {boolean} sub
 * @property {number} pct           progress ÷ finish line, 0-100
 * @property {boolean} finished
 * @property {number} boosts        what is left in the racer's !boost pool
 * @property {string|null} image    custom join image (URL or local path)
 * @property {string|null} avatar   picture URL the overlays show (/image/:login when `image` is set)
 * @property {number} [x]           world position, present while racing
 * @property {number} [z]
 */

/**
 * @typedef {object} Snapshot  GET /race, also the payload of the lobby / race_start / positions / race_end events.
 * @property {boolean} running
 * @property {boolean} [lobby]
 * @property {string|null} streamer  the streamer's login
 * @property {Vehicle[]} vehicles
 */

/**
 * @typedef {object} CameraState  GET /camera and the `camera` event.
 * @property {boolean} auto                        the director is on
 * @property {string|null} mode                    follow · free · chase · front · pack · sweep · side · high · orbit · overhead · prop · finish · manual
 * @property {string|null} target
 * @property {string[]} [cars]
 * @property {number} [fov]
 * @property {Record<string, boolean>} [shots]     director toggles, mirrors settings.camera.shots
 */

/**
 * @typedef {object} AutoJoinEntry  Someone who joins every lobby (settings.autoJoin). Shape = POST /join body.
 * @property {string} id
 * @property {string} login
 * @property {string} displayName
 * @property {string|null} color
 * @property {boolean} sub
 * @property {string|null} image
 * @property {boolean} [autoBoost]
 */

/**
 * @typedef {object} UiSettings  Page defaults, saved in settings.ui so every browser / OBS dock shares them.
 * @property {number|string} boomCount      Controls → Boom ×
 * @property {number|string} addBoosts      Controls → Add boosts +
 * @property {number|string} slowMult       Controls → Slow all ×
 * @property {number|string} slowSecs       Controls → Slow all sec
 * @property {number|string} boostForce     Controls → Boost all force ("" = the game's random strength)
 * @property {number|string} boostSecs
 * @property {boolean} cameraOnControls     show the camera buttons on the Controls page too
 * @property {string} [chatJoinUrl]         the bot route that adds everyone in chat
 */

/**
 * @typedef {object} Settings  GET /settings: everything persisted server-side. Only the keys the page uses are listed;
 * the ones in SETTINGS_KEYS are what PUT /settings receives from here, the rest (config, twitch, followerChecks…) is read-only.
 * @property {boolean} [autoJoinStreamer]
 * @property {string} [streamerColor]                 "" = the game picks
 * @property {AutoJoinEntry[]} autoJoin
 * @property {UiSettings} ui
 * @property {string[]} [bots]                        Twitch logins that race as AI cars (null = the built-in list)
 * @property {Array<{login: string, displayName: string, color: string|null, image: string|null}>} [customBots]
 * @property {Record<string, {autoBoost?: boolean}>} [botOptions]
 * @property {object} [overlay]                       Settings → Overlay look (defaults in overlay-shared.js)
 * @property {object} [minimap]                       Settings → Mini map
 * @property {{shots?: Record<string, boolean>}} [camera]
 * @property {object} [perks]
 * @property {Array<{event: string, url: string, method: string, header: string, body: string, enabled: boolean}>} [webhooks]
 * @property {boolean} [colorLeaderboard]
 * @property {boolean} [colorCommandEnabled]
 * @property {string} [colorCommand]                  alias list, `!race color|!color`
 * @property {boolean} [respawnCommandEnabled]
 * @property {number} [respawnLimit]
 * @property {string} [respawnCommand]
 * @property {Record<string, string>} [colors]        saved car colors by login
 * @property {string} [twitchClientId]
 * @property {boolean} [twitchTokenSet]
 * @property {object} [twitch]                        Twitch login status (GET /twitch/token)
 * @property {object} config                          plugin config: {port, tokenRequired, bindAll, hotkeyBoost, camUp, camDown, tickHz, posHz}
 */

/** @type {UiSettings} */
const DEFAULT_UI = { boomCount: 1, addBoosts: 1, slowMult: 0.3, slowSecs: 5, boostForce: "", boostSecs: "", cameraOnControls: false };

// settings.ui can be partial or missing; the pages always see every default.
const withUiDefaults = (settings) => ({ ...settings, ui: { ...DEFAULT_UI, ...settings.ui } });

// What PUT /settings receives from the page. The server merges, so this is exactly the part of /settings the page owns.
const SETTINGS_KEYS = [
  "autoJoinStreamer", "streamerColor", "autoJoin", "ui", "bots", "customBots", "overlay", "minimap", "camera",
  "colorLeaderboard", "colorCommandEnabled", "colorCommand", "respawnCommandEnabled", "respawnLimit", "respawnCommand",
  "colors", "perks", "twitchToken", "twitchClientId", "botOptions", "webhooks",
];
const pick = (object, keys) => Object.fromEntries(keys.map((key) => [key, object[key]]));

const pageFromLocation = () => location.pathname.replace(/^\//, "") || "controls";
if (location.hash.startsWith("#/")) history.replaceState(null, "", "/" + location.hash.slice(2)); // old #/page links

let eventSource, retryTimer;

export const useStore = create((set, get) => ({
  /** @type {string} */ page: pageFromLocation(),
  /** @type {Snapshot} */ snapshot: { running: false, streamer: null, vehicles: [] },
  /** @type {{id?: string, login?: string, inRace?: boolean, boosts?: number}} GET /me */ me: {},
  /** @type {{api?: string, commit?: string, latest?: string, upToDate?: boolean|null, updateUrl?: string|null}} GET /version */ version: {},
  /** @type {Settings} */ settings: { autoJoin: [], ui: DEFAULT_UI, config: {} },
  /** @type {boolean} the event stream is connected */ online: false,
  /** @type {string} the timing board's search box */ search: "",
  /** @type {CameraState} */ camera: { auto: false, mode: null, target: null },

  setPage: (page) => { history.pushState(null, "", "/" + page); set({ page }); },
  setSearch: (search) => set({ search }),
  /** settings.ui with every default filled in. @returns {UiSettings} */
  uiSettings: () => ({ ...DEFAULT_UI, ...get().settings.ui }),

  refresh: async () => {
    const snapshot = await api("/race", { method: "GET" });
    if (snapshot) set({ snapshot, online: true });
    const camera = await api("/camera", { method: "GET" });
    if (camera) set({ camera });
  },
  loadMe: async () => { const me = await api("/me", { method: "GET" }); if (me) set({ me }); },
  loadVersion: async () => { const version = await api("/version", { method: "GET" }); if (version) set({ version }); },
  loadSettings: async () => { const settings = await api("/settings", { method: "GET" }); if (settings) set({ settings: withUiDefaults(settings) }); },
  /** Merge `patch` into the settings and PUT the page-owned keys; the server answers with the full settings. */
  saveSettings: async (patch) => {
    const next = { ...get().settings, ...patch };
    const saved = await api("/settings", { method: "PUT", body: pick(next, SETTINGS_KEYS) });
    if (saved) set({ settings: withUiDefaults(saved) });
  },

  connect: () => {
    clearTimeout(retryTimer); eventSource?.close();
    eventSource = new EventSource(withToken("/events"));
    const payloadOf = (event) => JSON.parse(event.data);

    const onSnapshot = (event) => set({ snapshot: payloadOf(event), online: true });
    // Boost pools change between the 4 Hz snapshots (a !boost, a perk, /boost/:x/add): patch the count straight away.
    // The next `positions` snapshot replaces the whole field and stays the truth.
    const onBoostChange = (event) => {
      const { login, boosts } = payloadOf(event);
      if (typeof boosts !== "number" || !login) return;
      set((state) => ({
        snapshot: { ...state.snapshot, vehicles: state.snapshot.vehicles.map((vehicle) => (vehicle.login === login ? { ...vehicle, boosts } : vehicle)) },
        me: login === state.snapshot.streamer ? { ...state.me, boosts } : state.me,
      }));
    };
    const onCamera = (event) => set({ camera: payloadOf(event) });
    const onScreen = () => { get().refresh(); get().loadMe(); };
    const onSettings = (event) => set({ settings: withUiDefaults(payloadOf(event)) });

    for (const name of ["lobby", "race_start", "positions", "race_end"]) eventSource.addEventListener(name, onSnapshot);
    eventSource.addEventListener("joined", get().refresh);
    eventSource.addEventListener("finisher", get().refresh);
    eventSource.addEventListener("boost", onBoostChange);
    eventSource.addEventListener("boosts", onBoostChange);
    eventSource.addEventListener("camera", onCamera);
    eventSource.addEventListener("screen", onScreen);
    eventSource.addEventListener("settings", onSettings);
    eventSource.onopen = () => { set({ online: true }); get().refresh(); get().loadMe(); get().loadSettings(); get().loadVersion(); };
    // Chromium quietly stops retrying after a few refused connections; a game restart takes longer than that.
    eventSource.onerror = () => { set({ online: false }); eventSource.close(); retryTimer = setTimeout(get().connect, 3000); };
  },
}));

window.addEventListener("popstate", () => useStore.setState({ page: pageFromLocation() }));
