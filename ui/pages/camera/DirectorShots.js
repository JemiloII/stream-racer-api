// Camera → Director → "Director uses": one switch per settings.camera.shots key. A missing key means on; the
// director never picks a shot switched off here, manual POST /camera/<shot> still works.
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";

// settings.camera.shots key → label on the page. Order = the order of the switches.
export const DIRECTOR_SHOTS = [
  ["grid", "Grid start"], ["high", "High overview"], ["side", "Side"], ["sweep", "Sweep"], ["pack", "Pack"], ["front", "Front leader"], ["chase", "Chase leader"],
  ["orbit", "Orbit"], ["overhead", "Overhead"], ["prop", "Track cams"], ["finish", "Finish cam"], ["duel", "Duel"], ["pileup", "Pile-up"], ["boom", "Boom orbit"],
];
export const shotEnabled = (shots, key) => shots?.[key] !== false;

export default function DirectorShots() {
  const { settings, saveSettings } = useStore();
  const camera = settings.camera || {};
  const shots = camera.shots || {};
  // Save every key explicitly so a later default change on the server can't flip what the streamer chose.
  const setShot = (key, enabled) => saveSettings({ camera: { ...camera, shots: { ...Object.fromEntries(DIRECTOR_SHOTS.map(([shotKey]) => [shotKey, shotEnabled(shots, shotKey)])), [key]: enabled } } });
  return html`
    <div class="shots">
      <div class="shots-head">Director uses</div>
      ${DIRECTOR_SHOTS.map(([key, label]) => html`
        <label class="switch-row" key=${key}>
          <input type="checkbox" role="switch" checked=${shotEnabled(shots, key)} onChange=${(event) => setShot(key, event.target.checked)} />
          <span>${label}</span>
        </label>`)}
    </div>`;
}
