import { html, useStore, useCss, api, toast } from "../store.js";
import Racers from "../components/racers.js";
import { CameraControls } from "./camera.js";

// Module-level so re-renders (60/s during a race) don't remount the input and eat what's being typed.
const Num = ({ ui, setUi, k, prefix, width }) => html`
  <label class="field"><span>${prefix}</span><input style=${{ width: width || 54 }} defaultValue=${ui[k]} onBlur=${(e) => e.target.value !== String(ui[k]) && setUi(k, e.target.value)} onKeyDown=${(e) => e.key === "Enter" && e.target.blur()} /></label>`;

function Action({ label, hint, children }) {
  return html`
    <div class="action">
      <div class="label">${label}<small>${hint}</small></div>
      <div class="fields">${children}</div>
    </div>`;
}

export default function Controls() {
  useCss("pages/controls.css");
  const { snap, saveSettings } = useStore();
  const ui = useStore((s) => s.ui());
  const mine = snap.vehicles.find((x) => x.login === snap.streamer);
  const hotkey = useStore((s) => s.settings.config?.hotkeyBoost || "J");
  const setUi = (k, v) => saveSettings({ ui: { ...ui, [k]: v } });
  // Twitch's chatters list needs a moderator token the game doesn't have, so the bot does it (voisona-bot: POST /race/join-chat).
  const joinChat = async () => {
    try { const r = await fetch(ui.chatJoinUrl || "http://127.0.0.1:8788/race/join-chat", { method: "POST" }); const j = await r.json().catch(() => ({})); toast(r.ok ? `chat · ${j.affected ?? 0} joined` : j.error || `bot said ${r.status}`, !r.ok); }
    catch { toast("bot not reachable (Settings → Pages → chat join URL)", true); }
  };

  return html`
    <div class="grid2">
      <div class="stack">
        <article>
          <header>Driver</header>
          <button class="hero" onClick=${() => api("/boost/me")}>Boost me</button>
          <div class="hero-meta">
            <span>boosts left <b>${mine ? mine.boosts : "–"}</b></span>
            <span>hotkey <kbd>${hotkey}</kbd></span>
          </div>
        </article>

        <article>
          <header>Field</header>
          <${Action} label="Boom" hint="random cars, skips ones mid-boom">
            <${Num} ui=${ui} setUi=${setUi} k="boomCount" prefix="×" />
            <button class="boom" onClick=${() => api(`/boom/${ui.boomCount}`)}>Fire</button>
          <//>
          <${Action} label="Boost all" hint="free, doesn't touch their pool">
            <button class="boost" onClick=${() => api(`/boost/all${ui.boostForce ? `?force=${ui.boostForce}&seconds=${ui.boostSecs || 3}` : ""}`)}>Fire</button>
          <//>
          <${Action} label="Add boosts" hint="to everyone's !boost pool">
            <${Num} ui=${ui} setUi=${setUi} k="addBoosts" prefix="+" />
            <button class="boost" onClick=${() => api(`/boost/all/add?n=${ui.addBoosts}`)}>Add</button>
          <//>
          <${Action} label="Slow all" hint="top-speed multiplier for a while">
            <${Num} ui=${ui} setUi=${setUi} k="slowMult" prefix="×" />
            <${Num} ui=${ui} setUi=${setUi} k="slowSecs" prefix="sec" />
            <button class="slow" onClick=${() => api(`/speed/all?mult=${ui.slowMult}&seconds=${ui.slowSecs}`)}>Fire</button>
          <//>
          <${Action} label="Respawn all" hint="the game's stuck-car reset">
            <button class="secondary" onClick=${() => api("/respawn/all")}>Fire</button>
          <//>
          <${Action} label="Boom everyone" hint="except you">
            <button class="boom" onClick=${() => api(`/boom/all?except=${encodeURIComponent(snap.streamer || "")}`)}>Fire</button>
          <//>
        </article>

        ${ui.cameraOnControls ? html`<article><header>Camera</header><${CameraControls} /></article>` : null}

        <article>
          <header>Race</header>
          <div class="btn-row">
            <button class="secondary" onClick=${() => api("/lobby")}>Create lobby</button>
            <button onClick=${() => api("/race/start")} title="starts the lobby countdown, like the START button">Start countdown</button>
            <button class="secondary" onClick=${() => api("/race/start?now=1")} title="skip the countdown">Start now</button>
          </div>
          <div class="btn-row">
            <button class="secondary outline" onClick=${() => confirm("End the race now?") && api("/race/end")}>End race</button>
            <button class="secondary" onClick=${() => api("/race/next")}>Next random map</button>
          </div>
          <div class="btn-row">
            <button class="secondary" disabled=${snap.running} title="everyone in chat right now, lurkers included (through your bot)" onClick=${joinChat}>Add all of chat</button>
          </div>
        </article>
      </div>
      <${Racers} />
    </div>`;
}
