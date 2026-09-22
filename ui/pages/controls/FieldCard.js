// Controls → Field: the crowd actions on every car (boom, boost, add boosts, slow, respawn). The numbers next to
// them are page defaults saved in settings.ui, so every browser / OBS dock shares them.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

// Number field that commits on blur / Enter. Module-level on purpose: pages re-render 60×/s during a race and a
// component defined inside the render would remount the input, wiping what is being typed (see CLAUDE.md).
const NumberField = ({ values, name, onCommit, prefix, width }) => html`
  <label class="field"><span>${prefix}</span><input style=${{ width: width || 54 }} defaultValue=${values[name]} onBlur=${(event) => event.target.value !== String(values[name]) && onCommit(name, event.target.value)} onKeyDown=${(event) => event.key === "Enter" && event.target.blur()} /></label>`;

function ActionRow({ label, hint, children }) {
  return html`
    <div class="action">
      <div class="label">${label}<small>${hint}</small></div>
      <div class="fields">${children}</div>
    </div>`;
}

export default function FieldCard() {
  const { snapshot, saveSettings } = useStore();
  const uiSettings = useStore((state) => state.uiSettings());
  const setUiSetting = (name, value) => saveSettings({ ui: { ...uiSettings, [name]: value } });
  const boostAllPath = `/boost/all${uiSettings.boostForce ? `?force=${uiSettings.boostForce}&seconds=${uiSettings.boostSecs || 3}` : ""}`;
  return html`
    <article>
      <header>Field</header>
      <${ActionRow} label="Boom" hint="random cars, skips ones mid-boom">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="boomCount" prefix="×" />
        <button class="boom" onClick=${() => api(`/boom/${uiSettings.boomCount}`)}>Fire</button>
      <//>
      <${ActionRow} label="Boost all" hint="free, doesn't touch their pool">
        <button class="boost" onClick=${() => api(boostAllPath)}>Fire</button>
      <//>
      <${ActionRow} label="Add boosts" hint="to everyone's !boost pool">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="addBoosts" prefix="+" />
        <button class="boost" onClick=${() => api(`/boost/all/add?n=${uiSettings.addBoosts}`)}>Add</button>
      <//>
      <${ActionRow} label="Slow all" hint="top-speed multiplier for a while">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="slowMult" prefix="×" />
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="slowSecs" prefix="sec" />
        <button class="slow" onClick=${() => api(`/speed/all?mult=${uiSettings.slowMult}&seconds=${uiSettings.slowSecs}`)}>Fire</button>
      <//>
      <${ActionRow} label="Respawn all" hint="the game's stuck-car reset">
        <button class="secondary" onClick=${() => api("/respawn/all")}>Fire</button>
      <//>
      <${ActionRow} label="Boom everyone" hint="except you">
        <button class="boom" onClick=${() => api(`/boom/all?except=${encodeURIComponent(snapshot.streamer || "")}`)}>Fire</button>
      <//>
    </article>`;
}
