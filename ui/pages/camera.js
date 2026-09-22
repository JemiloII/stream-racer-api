// Camera page: every shot as a button, the director's per-shot switches, and the live timing board.
import { html, useCss } from "../lib/html.js";
import Racers from "../components/racers.js";
import { CameraControls } from "./camera/CameraControls.js";
import DirectorShots from "./camera/DirectorShots.js";

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
