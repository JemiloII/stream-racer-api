// Settings → Perks: who may use the chat color command, whose name is colored, and the extra boosts per tier
// (follower / subscriber / developer / host, stacking), plus the Twitch login / token for follower checks.
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";
import { NumberField, SelectField } from "./fields.js";
import TwitchConnect from "./TwitchConnect.js";

const TIERS = [["everyone", "everyone"], ["follower", "followers +"], ["subscriber", "subscribers +"], ["off", "off"]];
const PERK_DEFAULTS = { colorCommand: "follower", coloredNames: "everyone", boostFollower: 0, boostSubscriber: 0, boostDeveloper: 0, boostHost: 0 };

export default function PerksCard() {
  const { settings, saveSettings } = useStore();
  const perks = { ...PERK_DEFAULTS, ...(settings.perks || {}) };
  const setPerk = (key, value) => saveSettings({ perks: { ...perks, [key]: value } });
  const saveClientId = (event) => event.target.value.trim() !== (settings.twitchClientId || "") && saveSettings({ twitchClientId: event.target.value.trim() });
  const saveTokenOnEnter = (event) => { if (event.key === "Enter") { saveSettings({ twitchToken: event.target.value.trim() }); event.target.value = ""; } };
  return html`
    <article>
      <header>Perks</header>
      <p class="hint">Who gets what. Subscriber and developer come from the game; host is you; <b>follower needs the Twitch login below</b> (the game's own token can't check follows). Extra boosts stack: a subscribed follower gets both.</p>
      <div class="ovgrid">
        <${SelectField} values=${perks} onChange=${setPerk} options=${TIERS} name="colorCommand" label="Chat color command" />
        <${SelectField} values=${perks} onChange=${setPerk} options=${TIERS} name="coloredNames" label="Colored name on the leaderboard" />
        <${NumberField} values=${perks} onChange=${setPerk} name="boostFollower" step="1" label="Extra boosts: follower" />
        <${NumberField} values=${perks} onChange=${setPerk} name="boostSubscriber" step="1" label="Extra boosts: subscriber" />
        <${NumberField} values=${perks} onChange=${setPerk} name="boostDeveloper" step="1" label="Extra boosts: developer" />
        <${NumberField} values=${perks} onChange=${setPerk} name="boostHost" step="1" label="Extra boosts: host (you)" />
        <${TwitchConnect} />
        <label>Twitch client id (your app at dev.twitch.tv)<input key=${settings.twitchClientId || ""} defaultValue=${settings.twitchClientId || ""} spellCheck="false" onBlur=${saveClientId} /></label>
        <label>Twitch token ${settings.twitchTokenSet ? html`<small style=${{ color: "var(--live)" }}>· set</small>` : html`<small style=${{ color: "var(--dim)" }}>· not set, follower = never</small>`}<input type="password" placeholder=${settings.twitchTokenSet ? "•••••• (stored)" : "paste token"} onKeyDown=${saveTokenOnEnter} /></label>
        <p class="hint" style=${{ gridColumn: "1 / -1", margin: 0 }}>Press Enter to save the token (empty clears it). Followers are looked up once per login per session. Without a token, "followers +" only lets subs, devs and you through.</p>
      </div>
    </article>`;
}
