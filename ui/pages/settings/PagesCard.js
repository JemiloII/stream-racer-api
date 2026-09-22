// Settings → Pages: switches for the control page itself (camera buttons on Controls, the bot's chat-join URL).
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";

const DEFAULT_CHAT_JOIN_URL = "http://127.0.0.1:8788/race/join-chat";

export default function PagesCard() {
  const { settings, saveSettings } = useStore();
  const setUiSetting = (key, value) => saveSettings({ ui: { ...settings.ui, [key]: value } });
  const saveChatJoinUrl = (event) => event.target.value.trim() !== (settings.ui?.chatJoinUrl || "") && setUiSetting("chatJoinUrl", event.target.value.trim());
  return html`
    <article>
      <header>Pages</header>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${!!settings.ui?.cameraOnControls} onChange=${(event) => setUiSetting("cameraOnControls", event.target.checked)} />
        <span>Show the camera controls on the Controls page too</span>
      </label>
      <p class="hint">The Camera page always has them.</p>
      <label>Chat join URL <span class="hint-inline">the bot route that adds everyone in chat (Controls → Add all of chat)</span>
        <input defaultValue=${settings.ui?.chatJoinUrl || DEFAULT_CHAT_JOIN_URL} spellCheck="false" onBlur=${saveChatJoinUrl} />
      </label>
    </article>`;
}
