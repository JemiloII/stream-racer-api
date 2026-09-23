// Settings → Overlay look: settings.overlay in two groups, the horizontal bar (/overlay) and the leaderboard
// (/leaderboard, `board*` keys), applied live to every open browser source. Accent and side margin apply to both.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";
import { OVERLAY_DEFAULTS } from "../../lib/defaults.js";
import { NumberField } from "./fields.js";

// Browser-source link for the leaderboard: rows / side / scale as query params so one layout can have a small one and another a big one.
function LeaderboardLinkBuilder({ overlay }) {
  const [rows, setRows] = useState(overlay.board), [side, setSide] = useState(overlay.boardSide), [scale, setScale] = useState(overlay.boardScale), [copied, setCopied] = useState(false);
  const url = `${location.origin}/leaderboard?rows=${encodeURIComponent(rows)}&side=${encodeURIComponent(side)}&scale=${encodeURIComponent(scale)}`;
  const copy = () => navigator.clipboard.writeText(url).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1200); });
  return html`
    <div class="mmlink lblink">
      <label>Rows<input type="number" step="1" min="0" value=${rows} onInput=${(event) => setRows(event.target.value)} /></label>
      <label>Side<select value=${side} onChange=${(event) => setSide(event.target.value)}><option value="left">left</option><option value="right">right</option></select></label>
      <label>Scale<input type="number" step="0.1" min="0.2" value=${scale} onInput=${(event) => setScale(event.target.value)} /></label>
      <input readOnly value=${url} onFocus=${(event) => event.target.select()} />
      <button class="secondary" onClick=${copy}>${copied ? "Copied" : "Copy link"}</button>
    </div>`;
}

export default function OverlayCard() {
  const { settings, saveSettings } = useStore();
  const overlay = { ...OVERLAY_DEFAULTS, ...(settings.overlay || {}) };
  const setOverlay = (key, value) => saveSettings({ overlay: { ...overlay, [key]: value } });
  return html`
    <article>
      <header>Overlay look</header>
      <p class="hint">Two browser sources: <code>/overlay</code> (the horizontal bar) and <code>/leaderboard</code> (the vertical list), placed separately in OBS. Applies live to every open one, no refresh; query params on a URL still override. Both draw nothing outside a race and fade out when it ends.</p>
      <section class="ovgroup">
        <h4>Horizontal bar <span class="hint-inline"><code>/overlay</code> · avatars along a track line, RIP / boost / finish banner</span></h4>
        <div class="ovgrid">
          <${NumberField} values=${overlay} onChange=${setOverlay} name="size" step="1" label="Avatar size (px)" />
          <${NumberField} values=${overlay} onChange=${setOverlay} name="lineHeight" step="1" label="Track line height (px)" />
          <${NumberField} values=${overlay} onChange=${setOverlay} name="spread" step="1" label="Min gap between cars (px; -1 = auto, 0 = raw positions)" />
          <${NumberField} values=${overlay} onChange=${setOverlay} name="offsetY" step="1" label="Nudge the bar up/down (px, + is down)" />
          <${NumberField} values=${overlay} onChange=${setOverlay} name="side" step="1" label="Side margin (px, both sources)" />
          <label>Accent color (both sources)<input type="color" value=${overlay.accent} onChange=${(event) => setOverlay("accent", event.target.value)} /></label>
          <label>Track line color<input value=${overlay.line} onBlur=${(event) => event.target.value !== overlay.line && setOverlay("line", event.target.value)} /></label>
          <label class="switch-row"><input type="checkbox" role="switch" checked=${overlay.names} onChange=${(event) => setOverlay("names", event.target.checked)} /><span>Names under avatars</span></label>
          <label class="switch-row"><input type="checkbox" role="switch" checked=${overlay.banner} onChange=${(event) => setOverlay("banner", event.target.checked)} /><span>RIP / finish banner</span></label>
          <label class="switch-row"><input type="checkbox" role="switch" checked=${!!overlay.showInLobby} onChange=${(event) => setOverlay("showInLobby", event.target.checked)} /><span>Show the field in the lobby too (both sources; otherwise only while racing)</span></label>
        </div>
      </section>
      <section class="ovgroup">
        <h4>Leaderboard <span class="hint-inline"><code>/leaderboard</code> · vertical top-N list, its own browser source</span></h4>
        <div class="ovgrid">
          <${NumberField} values=${overlay} onChange=${setOverlay} name="board" step="1" label="Rows (0 = off)" />
          <${NumberField} values=${overlay} onChange=${setOverlay} name="boardScale" label="Size (scale, 1 = 100%)" step="0.1" />
          <label>Anchor side<select value=${overlay.boardSide} onChange=${(event) => setOverlay("boardSide", event.target.value)}><option value="left">left</option><option value="right">right</option></select></label>
          <p class="hint" style=${{ margin: "26px 0 0" }}>Make it smaller here (or with <code>scale=</code> on the link) so the mini map fits above it.</p>
        </div>
        <p class="hint" style=${{ margin: "14px 0 6px" }}>Browser source for OBS: the link's params override the saved rows / side / size for that source only.</p>
        <${LeaderboardLinkBuilder} overlay=${overlay} />
      </section>
      <div class="btn-row"><button class="secondary outline" onClick=${() => saveSettings({ overlay: {} })}>Reset</button></div>
    </article>`;
}
