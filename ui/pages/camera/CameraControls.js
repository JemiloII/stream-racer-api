// The camera buttons: the director toggle plus one button per shot. Shared by the Camera page and, when
// settings.ui.cameraOnControls is on, the Controls page. Any shot button takes over from the director for that shot.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

// label → POST /camera/<shot>; ?seconds=0 = stay on the shot until the next camera call.
const SHOT_BUTTONS = [
  ["Wide follow", "/camera/wide?seconds=0"], ["Grid", "/camera/grid?seconds=0"], ["Pack", "/camera/pack?seconds=0"], ["Sweep", "/camera/sweep?seconds=0"],
  ["Side", "/camera/side?seconds=0"], ["High", "/camera/high?seconds=0"], ["Front leader", "/camera/front?seconds=0"], ["Chase leader", "/camera/chase?seconds=0"],
  ["Orbit leader", "/camera/orbit?seconds=0"], ["Track cam", "/camera/prop?seconds=0"], ["Overhead", "/camera/overhead?seconds=0"], ["Boom cam", "/camera/boom?seconds=0"],
  ["Game follow", "/camera/leader?seconds=0"], ["Free", "/camera/free"],
];

const describeCamera = (camera) =>
  (camera.mode ? `now: ${camera.mode}${camera.target ? " · " + camera.target : ""}${camera.fov ? " · fov " + Math.round(camera.fov) : ""}` : "") + (camera.auto ? " · auto resumes after a focus" : "");

export function CameraControls() {
  const camera = useStore((state) => state.camera);
  return html`
    <div class="cam-row">
      <button class=${camera.auto ? "" : "secondary"} onClick=${() => api("/camera/auto")}>${camera.auto ? "Auto: ON" : "Auto: off"}</button>
      ${SHOT_BUTTONS.map(([label, path]) => html`<button key=${label} class="secondary" onClick=${() => api(path)}>${label}</button>`)}
    </div>
    <p class="hint" style=${{ margin: "8px 0 0" }}>${describeCamera(camera)}</p>`;
}
