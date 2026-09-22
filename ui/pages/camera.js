import { html, useStore, useCss, api } from "../store.js";
import Racers from "../components/racers.js";

// Camera shots. The director (Auto) runs its own coverage-driven cuts; any button here takes over for that shot.
export function CameraControls() {
  const cam = useStore((s) => s.camera);
  const b = (label, path, cls = "secondary") => html`<button class=${cls} onClick=${() => api(path)}>${label}</button>`;
  return html`
    <div class="cam-row">
      <button class=${cam.auto ? "" : "secondary"} onClick=${() => api("/camera/auto")}>${cam.auto ? "Auto: ON" : "Auto: off"}</button>
      ${b("Wide follow", "/camera/wide?seconds=0")}
      ${b("Grid", "/camera/grid?seconds=0")}
      ${b("Pack", "/camera/pack?seconds=0")}
      ${b("Sweep", "/camera/sweep?seconds=0")}
      ${b("Side", "/camera/side?seconds=0")}
      ${b("High", "/camera/high?seconds=0")}
      ${b("Front leader", "/camera/front?seconds=0")}
      ${b("Chase leader", "/camera/chase?seconds=0")}
      ${b("Orbit leader", "/camera/orbit?seconds=0")}
      ${b("Track cam", "/camera/prop?seconds=0")}
      ${b("Overhead", "/camera/overhead?seconds=0")}
      ${b("Boom cam", "/camera/boom?seconds=0")}
      ${b("Game follow", "/camera/leader?seconds=0")}
      ${b("Free", "/camera/free")}
    </div>
    <p class="hint" style=${{ margin: "8px 0 0" }}>${cam.mode ? `now: ${cam.mode}${cam.target ? " · " + cam.target : ""}${cam.fov ? " · fov " + Math.round(cam.fov) : ""}` : ""}${cam.auto ? " · auto resumes after a focus" : ""}</p>`;
}

// The shots the director may pick from (settings.camera.shots, name → bool; a missing key = on). Order = the labels on the page.
export const DIRECTOR_SHOTS = [
  ["grid", "Grid start"], ["high", "High overview"], ["side", "Side"], ["sweep", "Sweep"], ["pack", "Pack"], ["front", "Front leader"], ["chase", "Chase leader"],
  ["orbit", "Orbit"], ["overhead", "Overhead"], ["prop", "Track cams"], ["finish", "Finish cam"], ["duel", "Duel"], ["pileup", "Pile-up"], ["boom", "Boom orbit"],
];
export const shotEnabled = (shots, key) => shots?.[key] !== false;

function DirectorShots() {
  const { settings, saveSettings } = useStore();
  const camera = settings.camera || {};
  const shots = camera.shots || {};
  const setShot = (key, on) => saveSettings({ camera: { ...camera, shots: { ...Object.fromEntries(DIRECTOR_SHOTS.map(([k]) => [k, shotEnabled(shots, k)])), [key]: on } } });
  return html`
    <div class="shots">
      <div class="shots-head">Director uses</div>
      ${DIRECTOR_SHOTS.map(([key, label]) => html`
        <label class="switch-row" key=${key}>
          <input type="checkbox" role="switch" checked=${shotEnabled(shots, key)} onChange=${(e) => setShot(key, e.target.checked)} />
          <span>${label}</span>
        </label>`)}
    </div>`;
}

export default function CameraPage() {
  useCss("pages/controls.css");
  return html`
    <div class="grid2">
      <div class="stack">
        <article>
          <header>Camera</header>
          <${CameraControls} />
        </article>
        <article>
          <header>Director</header>
          <p class="hint" style=${{ margin: 0 }}>Auto keeps most of the field in frame and cuts when coverage drops: opening grid shot, high overview, pans, side and front shots, track cams when cars pass them, boom orbit, duel and pile-up shots, finish cam. Moving the game camera yourself pauses it for 5 s. Off by default.</p>
          <${DirectorShots} />
        </article>
      </div>
      <${Racers} />
    </div>`;
}
