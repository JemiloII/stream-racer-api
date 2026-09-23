// Controls → Driver: the streamer's own "Boost me" (= !boost / the in-game hotkey) with the boosts-left counter.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

export default function DriverCard() {
  const snapshot = useStore((state) => state.snapshot);
  const hotkey = useStore((state) => state.settings.config?.hotkeyBoost || "J");
  const myCar = snapshot.vehicles.find((vehicle) => vehicle.login === snapshot.streamer);
  return html`
    <article>
      <header>Driver</header>
      <button class="hero" onClick=${() => api("/boost/me")}>Boost me</button>
      <button class="secondary respawn-me" onClick=${() => api("/respawn/me")} title="the game's stuck-car reset for your own car (free, never counted)">Respawn me</button>
      <div class="hero-meta">
        <span>boosts left <b>${myCar ? myCar.boosts : "–"}</b></span>
        <span>hotkey <kbd>${hotkey}</kbd></span>
      </div>
    </article>`;
}
