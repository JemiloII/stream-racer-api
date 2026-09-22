import { useState, useEffect, useRef } from "react";
import { html, useStore, useCss, api, getToken, setToken } from "../store.js";
import { TwitchLookup, Card, Grid, Empty, useTwitchUsers } from "../components/roster.js";

// Number field that commits on blur/Enter. Module-level on purpose: see the note in CLAUDE.md about remounts.
function Num({ obj, k, label, step, set }) {
  const commit = (e) => { const v = +e.target.value; if (!Number.isNaN(v) && v !== obj[k]) set(k, v); };
  return html`<label>${label}<input type="number" step=${step || 0.01} defaultValue=${obj[k]} onBlur=${commit} onKeyDown=${(e) => e.key === "Enter" && e.target.blur()} /></label>`;
}
const Sel = ({ obj, k, label, set, opts }) => html`<label>${label}<select value=${obj[k]} onChange=${(e) => set(k, e.target.value)}>${opts.map(([v, l]) => html`<option key=${v} value=${v}>${l}</option>`)}</select></label>`;

const MM_DEFAULTS = { enabled: true, x: 0.02, y: 0.03, w: 0.18, h: 0, marker: 3, bg: "#000000", alpha: 0.55, track: "#ffffff", pad: 1.15, leaderBig: true, names: true, aspect: "1:1" };
const RATIOS = ["1:1", "16:9", "4:3", "21:9", "3:2", "9:16", "auto"];
// Browser-source links: one per OBS layout. Query params override the shared look, so you can have a 1:1 map on one
// scene and a 16:9 one on another without touching settings.
function MinimapLink() {
  const [aspect, setAspect] = useState("1:1"), [names, setNames] = useState(true), [big, setBig] = useState(true), [copied, setCopied] = useState(false);
  const url = `${location.origin}/minimap?aspect=${encodeURIComponent(aspect)}&names=${names ? 1 : 0}&leaderBig=${big ? 1 : 0}`;
  const copy = () => navigator.clipboard.writeText(url).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1200); });
  return html`
    <div class="mmlink">
      <label>Ratio<select value=${aspect} onChange=${(e) => setAspect(e.target.value)}>${RATIOS.map((a) => html`<option key=${a} value=${a}>${a === "auto" ? "auto (track shape)" : a}</option>`)}</select></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${names} onChange=${(e) => setNames(e.target.checked)} /><span>Names</span></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${big} onChange=${(e) => setBig(e.target.checked)} /><span>Big leader dot</span></label>
      <input readOnly value=${url} onFocus=${(e) => e.target.select()} />
      <button class="secondary" onClick=${copy}>${copied ? "Copied" : "Copy link"}</button>
    </div>`;
}
function MinimapForm() {
  const { settings, saveSettings } = useStore();
  const mm = { ...MM_DEFAULTS, ...(settings.minimap || {}) };
  const set = (k, v) => saveSettings({ minimap: { ...mm, [k]: v } });
  return html`
    <div class="ovgrid">
      <label class="switch-row"><input type="checkbox" role="switch" checked=${mm.enabled} onChange=${(e) => set("enabled", e.target.checked)} /><span>Show mini map during races</span></label>
      <${Num} obj=${mm} set=${set} k="x" label="Left (0-1)" />
      <${Num} obj=${mm} set=${set} k="y" label="Bottom (0-1)" />
      <${Num} obj=${mm} set=${set} k="w" label="Width (0-1)" />
      <${Sel} obj=${mm} k="aspect" label="Ratio (in-game and browser)" set=${set} opts=${RATIOS.map((a) => [a, a === "auto" ? "auto (track shape)" : a])} />
      <${Num} obj=${mm} set=${set} k="marker" label="Car dot size (% of map)" step="0.5" />
      <${Num} obj=${mm} set=${set} k="pad" label="Margin (1 = tight fit)" step="0.05" />
      <label>Box color<input type="color" value=${mm.bg} onChange=${(e) => set("bg", e.target.value)} /></label>
      <${Num} obj=${mm} set=${set} k="alpha" label="Box opacity (0-1)" step="0.05" />
      <label>Track color<input type="color" value=${mm.track} onChange=${(e) => set("track", e.target.value)} /></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${mm.leaderBig} onChange=${(e) => set("leaderBig", e.target.checked)} /><span>Bigger dot for the leader</span></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${mm.names} onChange=${(e) => set("names", e.target.checked)} /><span>Names next to dots (kept from overlapping)</span></label>
    </div>
    <p class="hint" style=${{ margin: "14px 0 6px" }}>Browser source for OBS: make one link per layout. Size the source to the ratio and the map fills it. Look settings above still apply unless the link overrides them.</p>
    <${MinimapLink} />`;
}

// Mirrors DEFAULTS in ui/overlay-shared.js. `board*` keys drive /leaderboard, the rest the bar; accent and side margin apply to both.
const OV_DEFAULTS = { size: 40, names: true, accent: "#ffd400", line: "rgba(255,255,255,.35)", lineHeight: 6, bottom: 28, side: 24, banner: true, showInLobby: false, board: 10, boardSide: "left", boardScale: 1 };
// Browser-source link for the leaderboard: rows / side / scale as query params so one layout can have a small one and another a big one.
function LeaderboardLink({ ov }) {
  const [rows, setRows] = useState(ov.board), [side, setSide] = useState(ov.boardSide), [scale, setScale] = useState(ov.boardScale), [copied, setCopied] = useState(false);
  const url = `${location.origin}/leaderboard?rows=${encodeURIComponent(rows)}&side=${encodeURIComponent(side)}&scale=${encodeURIComponent(scale)}`;
  const copy = () => navigator.clipboard.writeText(url).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1200); });
  return html`
    <div class="mmlink lblink">
      <label>Rows<input type="number" step="1" min="0" value=${rows} onInput=${(e) => setRows(e.target.value)} /></label>
      <label>Side<select value=${side} onChange=${(e) => setSide(e.target.value)}><option value="left">left</option><option value="right">right</option></select></label>
      <label>Scale<input type="number" step="0.1" min="0.2" value=${scale} onInput=${(e) => setScale(e.target.value)} /></label>
      <input readOnly value=${url} onFocus=${(e) => e.target.select()} />
      <button class="secondary" onClick=${copy}>${copied ? "Copied" : "Copy link"}</button>
    </div>`;
}
function OverlayForm() {
  const { settings, saveSettings } = useStore();
  const ov = { ...OV_DEFAULTS, ...(settings.overlay || {}) };
  const set = (k, v) => saveSettings({ overlay: { ...ov, [k]: v } });
  return html`
    <section class="ovgroup">
      <h4>Horizontal bar <span class="hint-inline"><code>/overlay</code> · avatars along a track line, RIP / boost / finish banner</span></h4>
      <div class="ovgrid">
        <${Num} obj=${ov} set=${set} k="size" step="1" label="Avatar size (px)" />
        <${Num} obj=${ov} set=${set} k="lineHeight" step="1" label="Track line height (px)" />
        <${Num} obj=${ov} set=${set} k="bottom" step="1" label="Track bottom offset (px)" />
        <${Num} obj=${ov} set=${set} k="side" step="1" label="Side margin (px, both sources)" />
        <label>Accent color (both sources)<input type="color" value=${ov.accent} onChange=${(e) => set("accent", e.target.value)} /></label>
        <label>Track line color<input value=${ov.line} onBlur=${(e) => e.target.value !== ov.line && set("line", e.target.value)} /></label>
        <label class="switch-row"><input type="checkbox" role="switch" checked=${ov.names} onChange=${(e) => set("names", e.target.checked)} /><span>Names under avatars</span></label>
        <label class="switch-row"><input type="checkbox" role="switch" checked=${ov.banner} onChange=${(e) => set("banner", e.target.checked)} /><span>RIP / finish banner</span></label>
        <label class="switch-row"><input type="checkbox" role="switch" checked=${!!ov.showInLobby} onChange=${(e) => set("showInLobby", e.target.checked)} /><span>Show the field in the lobby too (both sources; otherwise only while racing)</span></label>
      </div>
    </section>
    <section class="ovgroup">
      <h4>Leaderboard <span class="hint-inline"><code>/leaderboard</code> · vertical top-N list, its own browser source</span></h4>
      <div class="ovgrid">
        <${Num} obj=${ov} set=${set} k="board" step="1" label="Rows (0 = off)" />
        <${Num} obj=${ov} set=${set} k="boardScale" label="Size (scale, 1 = 100%)" step="0.1" />
        <label>Anchor side<select value=${ov.boardSide} onChange=${(e) => set("boardSide", e.target.value)}><option value="left">left</option><option value="right">right</option></select></label>
        <p class="hint" style=${{ margin: "26px 0 0" }}>Make it smaller here (or with <code>scale=</code> on the link) so the mini map fits above it.</p>
      </div>
      <p class="hint" style=${{ margin: "14px 0 6px" }}>Browser source for OBS: the link's params override the saved rows / side / size for that source only.</p>
      <${LeaderboardLink} ov=${ov} />
    </section>
    <div class="btn-row"><button class="secondary outline" onClick=${() => saveSettings({ overlay: {} })}>Reset</button></div>`;
}

// Unity KeyCode name from a keyboard event, for the key-capture fields.
function keyName(e) {
  const c = e.code || "";
  if (/^Key[A-Z]$/.test(c)) return c.slice(3);
  if (/^Digit\d$/.test(c)) return "Alpha" + c.slice(5);
  if (/^Numpad\d$/.test(c)) return "Keypad" + c.slice(6);
  if (/^F\d{1,2}$/.test(c)) return c;
  const map = { Space: "Space", Tab: "Tab", Enter: "Return", Escape: "Escape", Backspace: "Backspace", Delete: "Delete", Insert: "Insert", Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
    ArrowUp: "UpArrow", ArrowDown: "DownArrow", ArrowLeft: "LeftArrow", ArrowRight: "RightArrow", ShiftLeft: "LeftShift", ShiftRight: "RightShift", ControlLeft: "LeftControl", ControlRight: "RightControl",
    AltLeft: "LeftAlt", AltRight: "RightAlt", Minus: "Minus", Equal: "Equals", BracketLeft: "LeftBracket", BracketRight: "RightBracket", Semicolon: "Semicolon", Quote: "Quote", Backquote: "BackQuote",
    Backslash: "Backslash", Comma: "Comma", Period: "Period", Slash: "Slash", CapsLock: "CapsLock", NumpadAdd: "KeypadPlus", NumpadSubtract: "KeypadMinus", NumpadMultiply: "KeypadMultiply", NumpadDivide: "KeypadDivide", NumpadEnter: "KeypadEnter", NumpadDecimal: "KeypadPeriod" };
  return map[c] || null;
}

function KeyField({ label, value, onChange }) {
  const [capturing, setCapturing] = useState(false);
  return html`
    <label>${label}
      <input value=${capturing ? "press a key…" : value} readOnly onFocus=${() => setCapturing(true)} onBlur=${() => setCapturing(false)}
        onKeyDown=${(e) => { e.preventDefault(); const k = keyName(e); if (k) { onChange(k); e.target.blur(); } }} />
    </label>`;
}

function PluginConfig({ cfg }) {
  const [port, setPort] = useState(cfg.port || 8793);
  const [msg, setMsg] = useState("");
  useEffect(() => setPort(cfg.port || 8793), [cfg.port]);
  const apply = async (patch) => {
    const r = await api("/config", { method: "PUT", body: patch });
    if (!r) return;
    if (r.errors?.length) { setMsg(r.errors.join(", ")); return; }
    setMsg("");
    if (r.restarting) { setMsg("restarting API on " + r.url + " …"); setTimeout(() => { location.href = r.url + location.pathname.slice(1); }, 1800); }
  };
  return html`
    <div class="ovgrid">
      <label>API port
        <div class="portrow"><input type="number" min="1024" max="65535" value=${port} onInput=${(e) => setPort(+e.target.value)} onKeyDown=${(e) => e.key === "Enter" && apply({ port })} />
        <button disabled=${port === cfg.port} onClick=${() => apply({ port })}>Apply</button></div>
      </label>
      <label class="switch-row" style=${{ margin: "26px 0 0" }}><input type="checkbox" role="switch" checked=${!!cfg.bindAll} onChange=${(e) => apply({ bindAll: e.target.checked })} /><span>Listen on all interfaces (needs a token)</span></label>
      <${KeyField} label="Boost hotkey" value=${cfg.hotkeyBoost || ""} onChange=${(k) => apply({ hotkeyBoost: k })} />
      <${KeyField} label="Free cam up (Q/E still work)" value=${cfg.camUp || ""} onChange=${(k) => apply({ camUp: k })} />
      <${KeyField} label="Free cam down" value=${cfg.camDown || ""} onChange=${(k) => apply({ camDown: k })} />
      <label>Positions per second (pos event)<input type="number" step="1" defaultValue=${cfg.posHz ?? 60} onBlur=${(e) => +e.target.value !== cfg.posHz && apply({ posHz: +e.target.value })} /></label>
      <label>Snapshots per second (positions event)<input type="number" step="1" defaultValue=${cfg.tickHz ?? 4} onBlur=${(e) => +e.target.value !== cfg.tickHz && apply({ tickHz: +e.target.value })} /></label>
      ${msg ? html`<p class="hint" style=${{ gridColumn: "1 / -1", margin: 0, color: "var(--hazard)" }}>${msg}</p>` : null}
    </div>`;
}

const TIERS = [["everyone", "everyone"], ["follower", "followers +"], ["subscriber", "subscribers +"], ["off", "off"]];
function PerksForm() {
  const { settings, saveSettings } = useStore();
  const p = { colorCommand: "follower", coloredNames: "everyone", boostFollower: 0, boostSubscriber: 1, boostDeveloper: 1, boostHost: 0, ...(settings.perks || {}) };
  const set = (k, v) => saveSettings({ perks: { ...p, [k]: v } });
  const followOk = settings.config?.twitchTokenSet !== undefined ? settings.twitchTokenSet : settings.twitchTokenSet;
  return html`
    <div class="ovgrid">
      <${Sel} obj=${p} set=${set} opts=${TIERS} k="colorCommand" step="1" label="Chat color command" />
      <${Sel} obj=${p} set=${set} opts=${TIERS} k="coloredNames" step="1" label="Colored name on the leaderboard" />
      <${Num} obj=${p} set=${set} k="boostFollower" step="1" label="Extra boosts: follower" />
      <${Num} obj=${p} set=${set} k="boostSubscriber" step="1" label="Extra boosts: subscriber" />
      <${Num} obj=${p} set=${set} k="boostDeveloper" step="1" label="Extra boosts: developer" />
      <${Num} obj=${p} set=${set} k="boostHost" step="1" label="Extra boosts: host (you)" />
      <label>Twitch client id (for follower checks)<input defaultValue=${settings.twitchClientId || ""} onBlur=${(e) => e.target.value.trim() !== (settings.twitchClientId || "") && saveSettings({ twitchClientId: e.target.value.trim() })} /></label>
      <label>Twitch token ${settings.twitchTokenSet ? html`<small style=${{ color: "var(--live)" }}>· set</small>` : html`<small style=${{ color: "var(--dim)" }}>· not set, follower = never</small>`}<input type="password" placeholder=${settings.twitchTokenSet ? "•••••• (stored)" : "paste token"} onKeyDown=${(e) => { if (e.key === "Enter") { saveSettings({ twitchToken: e.target.value.trim() }); e.target.value = ""; } }} /></label>
      <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>Press Enter to save the token (empty clears it). Followers are looked up once per login per session. Without a token, "followers +" only lets subs, devs and you through.</p>
    </div>`;
}

const HOOK_EVENTS = ["race_end", "race_start", "lobby", "finisher", "boom", "boost", "crash", "recovered", "respawn", "joined", "screen", "*"];
function WebhooksForm() {
  const { settings, saveSettings } = useStore();
  const hooks = settings.webhooks || [];
  const put = (i, patch) => saveSettings({ webhooks: hooks.map((h, j) => (j === i ? { ...h, ...patch } : h)) });
  const blur = (i, key) => (e) => e.target.value !== (hooks[i][key] || "") && put(i, { [key]: e.target.value });
  return html`
    <div class="hooks">
      ${hooks.map((h, i) => html`
        <div class="hook" key=${i}>
          <input type="checkbox" role="switch" checked=${h.enabled !== false} onChange=${(e) => put(i, { enabled: e.target.checked })} title="enabled" />
          <select value=${h.event || "race_end"} onChange=${(e) => put(i, { event: e.target.value })}>${HOOK_EVENTS.map((ev) => html`<option value=${ev}>${ev === "*" ? "any event" : ev}</option>`)}</select>
          <select value=${h.method || "POST"} onChange=${(e) => put(i, { method: e.target.value })}>${["POST", "GET", "PUT"].map((m) => html`<option>${m}</option>`)}</select>
          <input placeholder="http://127.0.0.1:8788/speak" defaultValue=${h.url || ""} onBlur=${blur(i, "url")} spellCheck="false" />
          <input placeholder="Authorization: Bearer …" defaultValue=${h.header || ""} onBlur=${blur(i, "header")} spellCheck="false" />
          <input placeholder=${'body (empty = event JSON) e.g. {"text": "Race over!"}'} defaultValue=${h.body || ""} onBlur=${blur(i, "body")} spellCheck="false" />
          <button class="x" title="remove" onClick=${() => saveSettings({ webhooks: hooks.filter((_, j) => j !== i) })}>✕</button>
        </div>`)}
      <button class="secondary outline" onClick=${() => saveSettings({ webhooks: [...hooks, { event: "race_end", url: "", method: "POST", header: "", body: "", enabled: true }] })}>Add webhook</button>
    </div>`;
}

export default function Settings() {
  useCss("pages/settings.css");
  const { settings, saveSettings, snap, me } = useStore();
  const cfg = settings.config || {};

  const aj = settings.autoJoin || [];
  const botLogins = new Set([...(settings.bots || []), ...(settings.customBots || []).map((b) => b.login)]);
  const people = aj.filter((e) => !botLogins.has(e.login)); // bots are starred on the Bots page instead
  const botsStarred = aj.length - people.length;
  const users = useTwitchUsers(people.filter((e) => !e.image).map((e) => e.login));
  const avatars = Object.fromEntries(Object.entries(users).map(([l, u]) => [l, u.image]));
  const addAuto = (list) => saveSettings({ autoJoin: [...aj, ...list.filter((e) => !aj.some((x) => x.login === e.login))] });
  const addRacers = () => addAuto(snap.vehicles.map((r) => ({ login: r.login, id: r.id || "", displayName: r.displayName, color: r.color, sub: !!r.sub, image: r.image || null })));
  const addTwitch = (us) => addAuto(us.map((u) => ({ login: u.login, id: u.id, displayName: u.displayName, color: null, sub: false, image: null })));

  return html`
    <div class="settings">
      <article>
        <header>You</header>
        <label class="switch-row">
          <input type="checkbox" role="switch" checked=${settings.autoJoinStreamer} onChange=${(e) => saveSettings({ autoJoinStreamer: e.target.checked })} />
          <span>Put my own car in every lobby${me.login ? " (@" + me.login + ")" : ""}</span>
        </label>
        <p class="hint">Same as pressing JOIN GAME in the lobby, no typing your own info.</p>
        <div class="you-row">
          <label>My car color
            <div class="colorrow">
              <button class=${"secondary" + (!settings.streamerColor ? " on" : "")} onClick=${() => saveSettings({ streamerColor: settings.streamerColor ? "" : "#ff8a00" })} title="auto = the game picks">auto</button>
              <input type="color" value=${settings.streamerColor || "#ff8a00"} disabled=${!settings.streamerColor} onChange=${(e) => saveSettings({ streamerColor: e.target.value })} />
            </div>
          </label>
          <button class="secondary" onClick=${() => api("/join/me")}>Join me now</button>
        </div>
      </article>

      <article>
        <header>Auto-join list <span class="hint-inline">${people.length} people join every lobby</span></header>
        <p class="hint">Twitch viewers you want in every race, looked up here. Bots are handled on the Bots page (★ there = auto-join), so they don't show in this list.${botsStarred ? ` ${botsStarred} bot${botsStarred > 1 ? "s" : ""} auto-join from there.` : ""}</p>
        <${TwitchLookup} onAdd=${addTwitch} label="Add" />
        <div class="toolbar" style=${{ margin: "10px 0 12px" }}>
          <button class="go" disabled=${!aj.length || snap.running} onClick=${() => api("/autojoin/join")}>Join them all now</button>
          <button disabled=${!snap.vehicles.length} onClick=${addRacers}>Add everyone in the lobby</button>
          <button disabled=${!people.length} onClick=${() => confirm("Remove every viewer from the auto-join list? (bots keep their ★)") && saveSettings({ autoJoin: aj.filter((e) => botLogins.has(e.login)) })}>Clear</button>
        </div>
        ${people.length ? html`<${Grid}>
          ${people.map((e) => html`<${Card} key=${e.login} r=${{ ...e, kind: "twitch", image: e.image || avatars[e.login] || null }}
            badges=${["Auto-join"]}
            tools=${html`<span class="sp"></span><button class="x" title="remove" onClick=${() => saveSettings({ autoJoin: aj.filter((x) => x.login !== e.login) })}>✕</button>`} />`)}
        <//>` : html`<${Empty} text="No viewers on the list" />`}
      </article>

      <article>
        <header>Perks</header>
        <p class="hint">Who gets what. Subscriber and developer come from the game; host is you; <b>follower needs a Twitch token</b> with <code>moderator:read:followers</code> (the game's own token can't check follows), paste one from your overlay/bot app below. Extra boosts stack: a subscribed follower gets both.</p>
        <${PerksForm} />
      </article>

      <article>
        <header>Pages</header>
        <label class="switch-row">
          <input type="checkbox" role="switch" checked=${!!settings.ui?.cameraOnControls} onChange=${(e) => saveSettings({ ui: { ...settings.ui, cameraOnControls: e.target.checked } })} />
          <span>Show the camera controls on the Controls page too</span>
        </label>
        <p class="hint">The Camera page always has them.</p>
        <label>Chat join URL <span class="hint-inline">the bot route that adds everyone in chat (Controls → Add all of chat)</span>
          <input defaultValue=${settings.ui?.chatJoinUrl || "http://127.0.0.1:8788/race/join-chat"} spellCheck="false" onBlur=${(e) => e.target.value.trim() !== (settings.ui?.chatJoinUrl || "") && saveSettings({ ui: { ...settings.ui, chatJoinUrl: e.target.value.trim() } })} />
        </label>
      </article>

      <article>
        <header>Chat commands</header>
        <label class="switch-row">
          <input type="checkbox" role="switch" checked=${settings.respawnCommandEnabled !== false} onChange=${(e) => saveSettings({ respawnCommandEnabled: e.target.checked })} />
          <span>Viewers can respawn their own car from chat</span>
        </label>
        <div class="ovgrid">
          <label>Respawn command<input defaultValue=${settings.respawnCommand || "!race respawn"} onBlur=${(e) => e.target.value.trim() !== (settings.respawnCommand || "!race respawn") && saveSettings({ respawnCommand: e.target.value.trim() || "!race respawn" })} /></label>
          <p class="hint" style=${{ margin: "26px 0 0" }}>The game's stuck-car reset, for the racer who typed it. Only during a race.</p>
        </div>
      </article>

      <article>
        <header>Names & colors</header>
        <label class="switch-row">
          <input type="checkbox" role="switch" checked=${settings.colorLeaderboard !== false} onChange=${(e) => saveSettings({ colorLeaderboard: e.target.checked })} />
          <span>Color names on the in-game leaderboard with each car's color</span>
        </label>
        <label class="switch-row">
          <input type="checkbox" role="switch" checked=${settings.colorCommandEnabled !== false} onChange=${(e) => saveSettings({ colorCommandEnabled: e.target.checked })} />
          <span>Viewers can set their color from chat</span>
        </label>
        <div class="ovgrid">
          <label>Chat command<input defaultValue=${settings.colorCommand || "!race color"} onBlur=${(e) => e.target.value.trim() !== (settings.colorCommand || "!race color") && saveSettings({ colorCommand: e.target.value.trim() || "!color" })} /></label>
          <p class="hint" style=${{ margin: "26px 0 0" }}>e.g. <code>${settings.colorCommand || "!race color"} #ff8800</code> or <code>${settings.colorCommand || "!race color"} red</code>. Remembered per viewer and applied whenever they join. ${Object.keys(settings.colors || {}).length} saved.</p>
          ${Object.keys(settings.colors || {}).length ? html`<button class="secondary outline" onClick=${() => api("/color/reset")}>Forget all saved colors</button>` : null}
        </div>
      </article>

      <article>
        <header>Webhooks</header>
        <p class="hint">Run something when an event happens: call your bot, post to chat through it, hit any API. Empty body sends the event's JSON; otherwise <code>{json}</code> and <code>{event}</code> are filled in. Example: <code>race_end</code> → <code>POST http://127.0.0.1:8788/speak</code> with body <code>{"text": "Race over!"}</code>.</p>
        <${WebhooksForm} />
      </article>

      <article>
        <header>Mini map</header>
        <p class="hint">In-game: drawn inside the game window (so it's on stream). Position and width are fractions of the screen from the bottom-left; height follows the track's shape unless you set it. Browser: links below.</p>
        <${MinimapForm} />
      </article>

      <article>
        <header>Overlay look</header>
        <p class="hint">Two browser sources: <code>/overlay</code> (the horizontal bar) and <code>/leaderboard</code> (the vertical list), placed separately in OBS. Applies live to every open one, no refresh; query params on a URL still override. Both draw nothing outside a race and fade out when it ends.</p>
        <${OverlayForm} />
      </article>

      <article>
        <header>API token</header>
        <p class="hint">${cfg.tokenRequired
          ? "A token is required: every API call needs it. Bots send it as Authorization: Bearer <token> (or ?token= on the event stream)."
          : "No token set: the API is open to anything on this machine. Set one below to require it (applies immediately, no restart)."}</p>
        <div class="ovgrid">
          <label>Required token (server)<input type="text" placeholder="leave empty for no token" onKeyDown=${(e) => { if (e.key === "Enter") { const t = e.target.value.trim(); setToken(t); api("/config", { method: "PUT", body: { token: t } }).then(() => location.reload()); } }} /></label>
          <label>This browser's token<input type="password" placeholder="token" defaultValue=${getToken()} onChange=${(e) => { setToken(e.target.value.trim()); location.reload(); }} /></label>
          <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>Press Enter in the first box to set or clear the required token; it is also saved into this browser so the page keeps working.</p>
        </div>
      </article>

      <article>
        <header>Game plugin</header>
        <p class="hint">Saved to <code>BepInEx/config/shibiko.streamracer.api.cfg</code> and applied immediately. Changing the port restarts the API server and this page follows it.</p>
        <${PluginConfig} cfg=${cfg} />
        <p class="hint">Page defaults (boom count, slow multiplier, …) are saved automatically when you edit them on the Controls page and stored in <code>shibiko.streamracer.settings.json</code> next to the config.</p>
      </article>
    </div>`;
}
