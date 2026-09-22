// Settings → Webhooks: settings.webhooks, one row per hook {event, method, url, header, body, enabled}, fired by
// Plugin.Emit for that event ("*" = any).
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";

const HOOK_EVENTS = ["race_end", "race_start", "lobby", "finisher", "boom", "boost", "crash", "recovered", "respawn", "joined", "screen", "*"];
const HOOK_METHODS = ["POST", "GET", "PUT"];
const NEW_HOOK = { event: "race_end", url: "", method: "POST", header: "", body: "", enabled: true };

export default function WebhooksCard() {
  const { settings, saveSettings } = useStore();
  const hooks = settings.webhooks || [];
  const updateHook = (index, patch) => saveSettings({ webhooks: hooks.map((hook, otherIndex) => (otherIndex === index ? { ...hook, ...patch } : hook)) });
  const removeHook = (index) => saveSettings({ webhooks: hooks.filter((_, otherIndex) => otherIndex !== index) });
  const addHook = () => saveSettings({ webhooks: [...hooks, { ...NEW_HOOK }] });
  // Text fields save on blur, only when the text changed.
  const commitOnBlur = (index, field) => (event) => event.target.value !== (hooks[index][field] || "") && updateHook(index, { [field]: event.target.value });
  return html`
    <article>
      <header>Webhooks</header>
      <p class="hint">Run something when an event happens: call your bot, post to chat through it, hit any API. Empty body sends the event's JSON; otherwise <code>{json}</code> and <code>{event}</code> are filled in. Example: <code>race_end</code> → <code>POST http://127.0.0.1:8788/speak</code> with body <code>{"text": "Race over!"}</code>.</p>
      <div class="hooks">
        ${hooks.map((hook, index) => html`
          <div class="hook" key=${index}>
            <input type="checkbox" role="switch" checked=${hook.enabled !== false} onChange=${(event) => updateHook(index, { enabled: event.target.checked })} title="enabled" />
            <select value=${hook.event || "race_end"} onChange=${(event) => updateHook(index, { event: event.target.value })}>${HOOK_EVENTS.map((eventName) => html`<option value=${eventName}>${eventName === "*" ? "any event" : eventName}</option>`)}</select>
            <select value=${hook.method || "POST"} onChange=${(event) => updateHook(index, { method: event.target.value })}>${HOOK_METHODS.map((method) => html`<option>${method}</option>`)}</select>
            <input placeholder="http://127.0.0.1:8788/speak" defaultValue=${hook.url || ""} onBlur=${commitOnBlur(index, "url")} spellCheck="false" />
            <input placeholder="Authorization: Bearer …" defaultValue=${hook.header || ""} onBlur=${commitOnBlur(index, "header")} spellCheck="false" />
            <input placeholder=${'body (empty = event JSON) e.g. {"text": "Race over!"}'} defaultValue=${hook.body || ""} onBlur=${commitOnBlur(index, "body")} spellCheck="false" />
            <button class="x" title="remove" onClick=${() => removeHook(index)}>✕</button>
          </div>`)}
        <button class="secondary outline" onClick=${addHook}>Add webhook</button>
      </div>
    </article>`;
}
