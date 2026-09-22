// Controls → Race: lobby and race flow (create lobby, countdown, start now, end, next map) and "Add all of chat".
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { toast } from "../../lib/toast.js";
import { useStore } from "../../store.js";

const DEFAULT_CHAT_JOIN_URL = "http://127.0.0.1:8788/race/join-chat";

export default function RaceCard() {
  const snapshot = useStore((state) => state.snapshot);
  const chatJoinUrl = useStore((state) => state.uiSettings().chatJoinUrl);
  // Twitch's chatters list needs a moderator token the game doesn't have, so the bot does it (voisona-bot: POST /race/join-chat).
  const joinChat = async () => {
    try {
      const response = await fetch(chatJoinUrl || DEFAULT_CHAT_JOIN_URL, { method: "POST" });
      const payload = await response.json().catch(() => ({}));
      toast(response.ok ? `chat · ${payload.affected ?? 0} joined` : payload.error || `bot said ${response.status}`, !response.ok);
    } catch { toast("bot not reachable (Settings → Pages → chat join URL)", true); }
  };
  return html`
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
        <button class="secondary" disabled=${snapshot.running} title="everyone in chat right now, lurkers included (through your bot)" onClick=${joinChat}>Add all of chat</button>
      </div>
    </article>`;
}
