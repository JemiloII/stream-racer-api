// Settings → Names & colors: colored names on the in-game leaderboard, the viewers' color command, saved colors.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

const DEFAULT_COLOR_COMMAND = "!race color|!color";

export default function NamesAndColorsCard() {
  const { settings, saveSettings } = useStore();
  const colorCommand = settings.colorCommand || "!race color";
  const savedColorCount = Object.keys(settings.colors || {}).length;
  const saveColorCommand = (event) => event.target.value.trim() !== colorCommand && saveSettings({ colorCommand: event.target.value.trim() || DEFAULT_COLOR_COMMAND });
  return html`
    <article>
      <header>Names & colors</header>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.colorLeaderboard !== false} onChange=${(event) => saveSettings({ colorLeaderboard: event.target.checked })} />
        <span>Color names on the in-game leaderboard with each car's color</span>
      </label>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.colorCommandEnabled !== false} onChange=${(event) => saveSettings({ colorCommandEnabled: event.target.checked })} />
        <span>Viewers can set their color from chat</span>
      </label>
      <div class="ovgrid">
        <label>Chat command<input key=${settings.colorCommand || ""} defaultValue=${settings.colorCommand || DEFAULT_COLOR_COMMAND} onBlur=${saveColorCommand} /></label>
        <p class="hint" style=${{ margin: "26px 0 0" }}>e.g. <code>${colorCommand} #ff8800</code> or <code>${colorCommand} red</code>. Remembered per viewer and applied whenever they join. ${savedColorCount} saved.</p>
        ${savedColorCount ? html`<button class="secondary outline" onClick=${() => api("/color/reset")}>Forget all saved colors</button>` : null}
      </div>
    </article>`;
}
