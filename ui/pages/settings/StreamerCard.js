// Settings → You: put the streamer's own car in every lobby, its color, and "Join me now".
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

const DEFAULT_CAR_COLOR = "#ff8a00";

export default function StreamerCard() {
  const { settings, saveSettings, me } = useStore();
  return html`
    <article>
      <header>You</header>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.autoJoinStreamer} onChange=${(event) => saveSettings({ autoJoinStreamer: event.target.checked })} />
        <span>Put my own car in every lobby${me.login ? " (@" + me.login + ")" : ""}</span>
      </label>
      <p class="hint">Same as pressing JOIN GAME in the lobby, no typing your own info.</p>
      <div class="you-row">
        <label>My car color
          <div class="colorrow">
            <button class=${"secondary" + (!settings.streamerColor ? " on" : "")} onClick=${() => saveSettings({ streamerColor: settings.streamerColor ? "" : DEFAULT_CAR_COLOR })} title="auto = the game picks">auto</button>
            <input type="color" value=${settings.streamerColor || DEFAULT_CAR_COLOR} disabled=${!settings.streamerColor} onChange=${(event) => saveSettings({ streamerColor: event.target.value })} />
          </div>
        </label>
        <button class="secondary" onClick=${() => api("/join/me")}>Join me now</button>
      </div>
    </article>`;
}
