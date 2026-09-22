// Settings → Game plugin: the BepInEx config (port, bind, hotkeys, event rates) edited live through PUT /config.
// A port change restarts the API server and the page follows it to the new URL.
import { useState, useEffect } from "react";
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";

const DEFAULT_PORT = 8793;

// Unity KeyCode name from a keyboard event, for the key-capture fields.
const KEY_CODE_NAMES = { Space: "Space", Tab: "Tab", Enter: "Return", Escape: "Escape", Backspace: "Backspace", Delete: "Delete", Insert: "Insert", Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
  ArrowUp: "UpArrow", ArrowDown: "DownArrow", ArrowLeft: "LeftArrow", ArrowRight: "RightArrow", ShiftLeft: "LeftShift", ShiftRight: "RightShift", ControlLeft: "LeftControl", ControlRight: "RightControl",
  AltLeft: "LeftAlt", AltRight: "RightAlt", Minus: "Minus", Equal: "Equals", BracketLeft: "LeftBracket", BracketRight: "RightBracket", Semicolon: "Semicolon", Quote: "Quote", Backquote: "BackQuote",
  Backslash: "Backslash", Comma: "Comma", Period: "Period", Slash: "Slash", CapsLock: "CapsLock", NumpadAdd: "KeypadPlus", NumpadSubtract: "KeypadMinus", NumpadMultiply: "KeypadMultiply", NumpadDivide: "KeypadDivide", NumpadEnter: "KeypadEnter", NumpadDecimal: "KeypadPeriod" };
function unityKeyName(event) {
  const code = event.code || "";
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return "Alpha" + code.slice(5);
  if (/^Numpad\d$/.test(code)) return "Keypad" + code.slice(6);
  if (/^F\d{1,2}$/.test(code)) return code;
  return KEY_CODE_NAMES[code] || null;
}

// Read-only box that captures the next key pressed while focused.
function KeyField({ label, value, onChange }) {
  const [capturing, setCapturing] = useState(false);
  const capture = (event) => { event.preventDefault(); const keyName = unityKeyName(event); if (keyName) { onChange(keyName); event.target.blur(); } };
  return html`
    <label>${label}
      <input value=${capturing ? "press a key…" : value} readOnly onFocus=${() => setCapturing(true)} onBlur=${() => setCapturing(false)} onKeyDown=${capture} />
    </label>`;
}

export default function PluginConfigCard() {
  const config = useStore((state) => state.settings.config || {});
  const [port, setPort] = useState(config.port || DEFAULT_PORT);
  const [message, setMessage] = useState("");
  useEffect(() => setPort(config.port || DEFAULT_PORT), [config.port]);
  const apply = async (patch) => {
    const response = await api("/config", { method: "PUT", body: patch });
    if (!response) return;
    if (response.errors?.length) { setMessage(response.errors.join(", ")); return; }
    setMessage("");
    if (response.restarting) { setMessage("restarting API on " + response.url + " …"); setTimeout(() => { location.href = response.url + location.pathname.slice(1); }, 1800); }
  };
  return html`
    <article>
      <header>Game plugin</header>
      <p class="hint">Saved to <code>BepInEx/config/shibiko.streamracer.api.cfg</code> and applied immediately. Changing the port restarts the API server and this page follows it.</p>
      <div class="ovgrid">
        <label>API port
          <div class="portrow"><input type="number" min="1024" max="65535" value=${port} onInput=${(event) => setPort(+event.target.value)} onKeyDown=${(event) => event.key === "Enter" && apply({ port })} />
          <button disabled=${port === config.port} onClick=${() => apply({ port })}>Apply</button></div>
        </label>
        <label class="switch-row" style=${{ margin: "26px 0 0" }}><input type="checkbox" role="switch" checked=${!!config.bindAll} onChange=${(event) => apply({ bindAll: event.target.checked })} /><span>Listen on all interfaces (needs a token)</span></label>
        <${KeyField} label="Boost hotkey" value=${config.hotkeyBoost || ""} onChange=${(keyName) => apply({ hotkeyBoost: keyName })} />
        <${KeyField} label="Free cam up (Q/E still work)" value=${config.camUp || ""} onChange=${(keyName) => apply({ camUp: keyName })} />
        <${KeyField} label="Free cam down" value=${config.camDown || ""} onChange=${(keyName) => apply({ camDown: keyName })} />
        <label>Positions per second (pos event)<input type="number" step="1" defaultValue=${config.posHz ?? 60} onBlur=${(event) => +event.target.value !== config.posHz && apply({ posHz: +event.target.value })} /></label>
        <label>Snapshots per second (positions event)<input type="number" step="1" defaultValue=${config.tickHz ?? 4} onBlur=${(event) => +event.target.value !== config.tickHz && apply({ tickHz: +event.target.value })} /></label>
        ${message ? html`<p class="hint" style=${{ gridColumn: "1 / -1", margin: 0, color: "var(--hazard)" }}>${message}</p>` : null}
      </div>
      <p class="hint">Page defaults (boom count, slow multiplier, …) are saved automatically when you edit them on the Controls page and stored in <code>shibiko.streamracer.settings.json</code> next to the config.</p>
    </article>`;
}
