// Settings → Perks → Connect Twitch: log the mod into Twitch with the streamer's own app. Follower checks and
// chat replies need scopes the game's token lacks (GET /twitch/auth, GET|DELETE /twitch/token).
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

const DEFAULT_REDIRECT_URI = "http://localhost:8793/twitch/callback";
const OnOff = ({ on }) => (on ? html`<span style=${{ color: "var(--live)" }}>on</span>` : html`<span style=${{ color: "var(--boom)" }}>off</span>`);

export default function TwitchConnect() {
  const { settings } = useStore();
  const twitch = settings.twitch || {};
  const disconnect = async () => { if (confirm("Forget the Twitch login?")) { await api("/twitch/token", { method: "DELETE" }); } };
  return html`
    <div class="twitch" style=${{ gridColumn: "1 / -1" }}>
      ${twitch.connected
        ? html`<p class="hint" style=${{ margin: 0 }}>Connected as <b>@${twitch.login}</b> · follower checks <${OnOff} on=${twitch.features?.followerChecks} /> · chat replies <${OnOff} on=${twitch.features?.chatReplies} />${twitch.missing?.length ? html` · missing: <code>${twitch.missing.join(" ")}</code>` : null}</p>`
        : html`<p class="hint" style=${{ margin: 0 }}>Not connected: follower checks and chat replies are off. Add <code>${twitch.redirectUri || DEFAULT_REDIRECT_URI}</code> to your Twitch app's OAuth Redirect URLs, put the client id below, then connect.</p>`}
      <div class="toolbar" style=${{ marginTop: 8 }}>
        <button class="go" disabled=${!settings.twitchClientId} onClick=${() => window.open("/twitch/auth", "_blank")}>${twitch.connected ? "Reconnect (re-auth)" : "Connect Twitch"}</button>
        ${twitch.connected ? html`<button onClick=${disconnect}>Disconnect</button>` : null}
      </div>
    </div>`;
}
