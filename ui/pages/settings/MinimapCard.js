// Settings → Mini map: settings.minimap (the in-game picture-in-picture map and the /minimap browser source share
// it) plus a link builder for OBS.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";
import { MINIMAP_DEFAULTS, MINIMAP_RATIOS } from "../../lib/defaults.js";
import { NumberField, SelectField } from "./fields.js";
import AlignPicker from "./AlignPicker.js";

const ratioLabel = (ratio) => (ratio === "auto" ? "auto (track shape)" : ratio);

// Browser-source links: one per OBS layout. Query params override the shared look, so you can have a 1:1 map on one
// scene and a 16:9 one on another without touching settings.
function MinimapLinkBuilder() {
  const [aspect, setAspect] = useState("1:1"), [names, setNames] = useState(true), [leaderBig, setLeaderBig] = useState(true), [copied, setCopied] = useState(false);
  const url = `${location.origin}/minimap?aspect=${encodeURIComponent(aspect)}&names=${names ? 1 : 0}&leaderBig=${leaderBig ? 1 : 0}`;
  const copy = () => navigator.clipboard.writeText(url).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1200); });
  return html`
    <div class="mmlink">
      <label>Ratio<select value=${aspect} onChange=${(event) => setAspect(event.target.value)}>${MINIMAP_RATIOS.map((ratio) => html`<option key=${ratio} value=${ratio}>${ratioLabel(ratio)}</option>`)}</select></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${names} onChange=${(event) => setNames(event.target.checked)} /><span>Names</span></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${leaderBig} onChange=${(event) => setLeaderBig(event.target.checked)} /><span>Big leader dot</span></label>
      <input readOnly value=${url} onFocus=${(event) => event.target.select()} />
      <button class="secondary" onClick=${copy}>${copied ? "Copied" : "Copy link"}</button>
    </div>`;
}

export default function MinimapCard() {
  const { settings, saveSettings } = useStore();
  const minimap = { ...MINIMAP_DEFAULTS, ...(settings.minimap || {}) };
  const setMinimap = (key, value) => saveSettings({ minimap: { ...minimap, [key]: value } });
  return html`
    <article>
      <header>Mini map</header>
      <p class="hint">In-game: drawn inside the game window (so it's on stream). Position and width are fractions of the screen from the bottom-left; height follows the track's shape unless you set it. Browser: links below.</p>
      <div class="ovgrid">
        <label class="switch-row"><input type="checkbox" role="switch" checked=${minimap.enabled} onChange=${(event) => setMinimap("enabled", event.target.checked)} /><span>Show the mini map inside the game (the /minimap browser source always works)</span></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${minimap.mapTitle !== false} onChange=${(event) => setMinimap("mapTitle", event.target.checked)} /><span>Map name above the map (in game and browser)</span></label>
      <label class="switch-row"><input type="checkbox" role="switch" checked=${!!minimap.mapAuthor} onChange=${(event) => setMinimap("mapAuthor", event.target.checked)} /><span>Show who built the map on a line of its own</span></label>
      <div class="aligns">
        <${AlignPicker} label="Map name" value=${minimap.mapTitleAlign || "center"} onChange=${(side) => setMinimap("mapTitleAlign", side)} />
        <${AlignPicker} label="Author" value=${minimap.mapAuthorAlign || "center"} onChange=${(side) => setMinimap("mapAuthorAlign", side)} />
      </div>
        <${NumberField} values=${minimap} onChange=${setMinimap} name="x" label="Left (0-1)" />
        <${NumberField} values=${minimap} onChange=${setMinimap} name="y" label="Bottom (0-1)" />
        <${NumberField} values=${minimap} onChange=${setMinimap} name="w" label="Width (0-1)" />
        <${SelectField} values=${minimap} name="aspect" label="Ratio (in-game and browser)" onChange=${setMinimap} options=${MINIMAP_RATIOS.map((ratio) => [ratio, ratioLabel(ratio)])} />
        <${NumberField} values=${minimap} onChange=${setMinimap} name="marker" label="Car dot size (% of map)" step="0.5" />
        <${NumberField} values=${minimap} onChange=${setMinimap} name="pad" label="Margin (1 = tight fit)" step="0.05" />
        <label>Box color<input type="color" value=${minimap.bg} onChange=${(event) => setMinimap("bg", event.target.value)} /></label>
        <${NumberField} values=${minimap} onChange=${setMinimap} name="alpha" label="Box opacity (0-1)" step="0.05" />
        <label>Track color<input type="color" value=${minimap.track} onChange=${(event) => setMinimap("track", event.target.value)} /></label>
        <label class="switch-row"><input type="checkbox" role="switch" checked=${minimap.leaderBig} onChange=${(event) => setMinimap("leaderBig", event.target.checked)} /><span>Bigger dot for the leader</span></label>
        <label class="switch-row"><input type="checkbox" role="switch" checked=${minimap.names} onChange=${(event) => setMinimap("names", event.target.checked)} /><span>Names next to dots (kept from overlapping)</span></label>
      </div>
      <p class="hint" style=${{ margin: "14px 0 6px" }}>Browser source for OBS: make one link per layout. Size the source to the ratio and the map fills it. Look settings above still apply unless the link overrides them.</p>
      <${MinimapLinkBuilder} />
    </article>`;
}
