// Controls → Field: the crowd actions on every car (boom, boost, add boosts, slow, shrink / grow, respawn). The numbers next to
// them are page defaults saved in settings.ui, so every browser / OBS dock shares them.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

// Number field that commits on blur / Enter. Module-level on purpose: pages re-render 60×/s during a race and a
// component defined inside the render would remount the input, wiping what is being typed (see the project notes).
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
  const { snapshot, settings, saveSettings } = useStore();
  const ranked = settings.sendResults !== false;   // Ranked on: these are all the mod's extras, refused by the API
  const uiSettings = useStore((state) => state.uiSettings());
  const setUiSetting = (name, value) => saveSettings({ ui: { ...uiSettings, [name]: value } });
  const boostAllPath = `/boost/all${uiSettings.boostForce ? `?force=${uiSettings.boostForce}&seconds=${uiSettings.boostSecs || 3}` : ""}`;
  return html`
    <article>
      <header>Field</header>
      ${ranked ? html`<p class="hint">Ranked is on: only the game's own racing. Turn Ranked off (Race card) to use these.</p>` : null}
      <${ActionRow} label="Boom" hint="random cars, skips ones mid-boom">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="boomCount" prefix="×" />
        <button class="boom" disabled=${ranked} onClick=${() => api(`/boom/${uiSettings.boomCount}`)}>Fire</button>
      <//>
      <${ActionRow} label="Boost all" hint="free, doesn't touch their pool">
        <button class="boost" disabled=${ranked} onClick=${() => api(boostAllPath)}>Fire</button>
      <//>
      <${ActionRow} label="Add boosts" hint="to everyone's !boost pool">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="addBoosts" prefix="+" />
        <button class="boost" disabled=${ranked} onClick=${() => api(`/boost/all/add?n=${uiSettings.addBoosts}`)}>Add</button>
      <//>
      <${ActionRow} label="Slow all" hint="top-speed multiplier for a while">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="slowMult" prefix="×" />
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="slowSecs" prefix="sec" />
        <button class="slow" disabled=${ranked} onClick=${() => api(`/speed/all?mult=${uiSettings.slowMult}&seconds=${uiSettings.slowSecs}`)}>Fire</button>
      <//>
      <${ActionRow} label="Shrink" hint="random cars, the game's shrink ray (12 s)">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="shrinkCount" prefix="×" />
        <button class="slow" disabled=${ranked} onClick=${() => api(`/shrink/${uiSettings.shrinkCount}`)}>Fire</button>
      <//>
      <${ActionRow} label="Grow" hint="random cars, giant for 12 s">
        <${NumberField} values=${uiSettings} onCommit=${setUiSetting} name="growCount" prefix="×" />
        <button class="boost" disabled=${ranked} onClick=${() => api(`/grow/${uiSettings.growCount}`)}>Fire</button>
      <//>
      <${ActionRow} label="Chaos" hint="the game's own effects">
        <button class="secondary" disabled=${ranked} onClick=${() => api("/effect/bananas")}>Bananas</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/effect/oil")}>Oil</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/effect/cones")}>Cones</button>
        <button class="boom" disabled=${ranked} onClick=${() => api("/effect/nuke")}>Nuke</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/effect/vsc")}>VSC</button>
      <//>
      <${ActionRow} label="Weather" hint="rain (puddles + lightning), snow (white map + snow/ice), fog">
        <button class="secondary" disabled=${ranked} onClick=${() => api("/weather/rain")}>Rain</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/weather/snow")}>Snow</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/weather/fog")}>Fog</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/weather/clear")}>Clear</button>
      <//>
      <${ActionRow} label="Road" hint="lay on the road ahead of the field">
        <button class="secondary" disabled=${ranked} onClick=${() => api("/surface/ice")}>Ice</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/surface/puddles")}>Puddles</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/surface/mud")}>Mud</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/surface/snow")}>Snow</button>
        <button class="secondary" disabled=${ranked} onClick=${() => api("/surface/clear")}>Clear</button>
      <//>
      <${ActionRow} label="Respawn all" hint="the game's stuck-car reset">
        <button class="secondary" disabled=${ranked} onClick=${() => api("/respawn/all")}>Fire</button>
      <//>
      <${ActionRow} label="Boom everyone" hint="except you">
        <button class="boom" disabled=${ranked} onClick=${() => api(`/boom/all?except=${encodeURIComponent(snapshot.streamer || "")}`)}>Fire</button>
      <//>
    </article>`;
}
