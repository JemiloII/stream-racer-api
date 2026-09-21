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
        </article>
      </div>
      <${Racers} />
    </div>`;
}
