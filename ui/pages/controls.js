// Controls page: the streamer's own boost, the crowd actions on the whole field, the camera buttons (when
// settings.ui.cameraOnControls is on), the lobby / race flow, and the live timing board.
import { html, useCss } from "../lib/html.js";
import { useStore } from "../store.js";
import Racers from "../components/racers.js";
import { CameraControls } from "./camera/CameraControls.js";
import DriverCard from "./controls/DriverCard.js";
import FieldCard from "./controls/FieldCard.js";
import RaceCard from "./controls/RaceCard.js";

export default function ControlsPage() {
  useCss("pages/controls.css");
  const cameraOnControls = useStore((state) => state.uiSettings().cameraOnControls);
  return html`
    <div class="grid2">
      <div class="stack">
        <${DriverCard} />
        <${FieldCard} />
        ${cameraOnControls ? html`<article><header>Camera</header><${CameraControls} /></article>` : null}
        <${RaceCard} />
      </div>
      <${Racers} />
    </div>`;
}
