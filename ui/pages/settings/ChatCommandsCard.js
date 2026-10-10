// Settings → Chat commands: the viewers' respawn command (alias list), the per-race respawn limit and the drift command.
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";

const DEFAULT_RESPAWN_COMMAND = "!race respawn|!respawn";
const DEFAULT_RESPAWN_LIMIT = 0;
const DEFAULT_DRIFT_COMMAND = "!race drift|!drift";

export default function ChatCommandsCard() {
  const { settings, saveSettings } = useStore();
  const saveRespawnCommand = (event) => event.target.value.trim() !== (settings.respawnCommand || "!race respawn") && saveSettings({ respawnCommand: event.target.value.trim() || DEFAULT_RESPAWN_COMMAND });
  const saveDriftCommand = (event) => event.target.value.trim() !== (settings.driftCommand || "") && saveSettings({ driftCommand: event.target.value.trim() || DEFAULT_DRIFT_COMMAND });
  const saveRespawnLimit = (event) => +event.target.value !== (settings.respawnLimit ?? DEFAULT_RESPAWN_LIMIT) && saveSettings({ respawnLimit: Math.max(-1, +event.target.value || 0) });
  return html`
    <article>
      <header>Chat commands</header>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.respawnCommandEnabled !== false} onChange=${(event) => saveSettings({ respawnCommandEnabled: event.target.checked })} />
        <span>Viewers can respawn their own car from chat</span>
      </label>
      <div class="ovgrid">
        <label>Respawn command<input key=${settings.respawnCommand || ""} defaultValue=${settings.respawnCommand || DEFAULT_RESPAWN_COMMAND} onBlur=${saveRespawnCommand} /></label>
        <label>Show-my-name command<input key=${settings.showCommand || ""} defaultValue=${settings.showCommand || "!race show|!show"} spellCheck="false" onBlur=${(event) => event.target.value.trim() !== (settings.showCommand || "") && saveSettings({ showCommand: event.target.value.trim() || "!race show|!show" })} /></label>
          <label>Chat respawns per racer per race (0 = off, -1 = unlimited)<input type="number" min="-1" step="1" key=${settings.respawnLimit ?? ""} defaultValue=${settings.respawnLimit ?? DEFAULT_RESPAWN_LIMIT} onBlur=${saveRespawnLimit} /></label>
        <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>The game's stuck-car reset, for the racer who typed it. Only during a race. Your own respawns from the Controls page are not counted. <code>!race inv</code> (through the bot) tells a viewer what they have left.</p>
      </div>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.driftCommandEnabled !== false} onChange=${(event) => saveSettings({ driftCommandEnabled: event.target.checked })} />
        <span>Viewers can drift their own car through the next corner</span>
      </label>
      <div class="ovgrid">
        <label>Drift command<input key=${settings.driftCommand || ""} defaultValue=${settings.driftCommand || DEFAULT_DRIFT_COMMAND} spellCheck="false" onBlur=${saveDriftCommand} /></label>
        <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>Costs one of that racer's boosts (given back if no corner comes within 10 s). The car slides through its next corner and gets a mini-turbo on the way out: the longer the slide, the bigger.</p>
      </div>
    </article>`;
}
