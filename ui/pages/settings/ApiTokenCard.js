// Settings → API token: the token the server requires (api.Token, via PUT /config) and the one this browser sends.
import { html } from "../../lib/html.js";
import { api, getToken, setToken } from "../../lib/api.js";
import { useStore } from "../../store.js";

export default function ApiTokenCard() {
  const tokenRequired = useStore((state) => !!state.settings.config?.tokenRequired);
  // The required token is also saved into this browser so the page keeps working after the reload.
  const setRequiredTokenOnEnter = (event) => {
    if (event.key !== "Enter") return;
    const token = event.target.value.trim();
    setToken(token);
    api("/config", { method: "PUT", body: { token } }).then(() => location.reload());
  };
  const setBrowserToken = (event) => { setToken(event.target.value.trim()); location.reload(); };
  return html`
    <article>
      <header>API token</header>
      <p class="hint">${tokenRequired
        ? "A token is required: every API call needs it. Bots send it as Authorization: Bearer <token> (or ?token= on the event stream)."
        : "No token set: the API is open to anything on this machine. Set one below to require it (applies immediately, no restart)."}</p>
      <div class="ovgrid">
        <label>Required token (server)<input type="text" placeholder="leave empty for no token" onKeyDown=${setRequiredTokenOnEnter} /></label>
        <label>This browser's token<input type="password" placeholder="token" defaultValue=${getToken()} onChange=${setBrowserToken} /></label>
        <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>Press Enter in the first box to set or clear the required token; it is also saved into this browser so the page keeps working.</p>
      </div>
    </article>`;
}
