// Bots page: one roster of Twitch bots (resolved live) and custom bots (your own login + picture) side by side.
// Card tools: ★ auto-join every lobby · ↯ auto-boost (off = a third party drives its pool via the API) · ✕ remove.
// Badges say what's on. Custom and auto-join bots sort first.
import { useState } from "react";
import { html, useCss } from "../lib/html.js";
import { api } from "../lib/api.js";
import { readFileAsDataUrl } from "../lib/files.js";
import { useStore } from "../store.js";
import { TwitchLookup, Grid, Empty, useTwitchUsers } from "../components/roster.js";
import AddCustomBotForm from "./bots/AddCustomBotForm.js";
import BotCard from "./bots/BotCard.js";

export default function BotsPage() {
  useCss("pages/bots.css");
  const { snapshot, settings, saveSettings } = useStore();
  const twitchLogins = settings.bots || [];
  const customBots = settings.customBots || [];
  const botOptions = settings.botOptions || {};
  const autoJoinLogins = new Set((settings.autoJoin || []).map((entry) => entry.login));
  const lobbyLogins = new Set(snapshot.vehicles.map((vehicle) => vehicle.login));
  const twitchUsers = useTwitchUsers(twitchLogins);
  const [pickedLogins, setPickedLogins] = useState(new Set());

  const roster = [
    ...customBots.map((bot) => ({ kind: "custom", ...bot })),
    ...twitchLogins.map((login) => { const user = twitchUsers[login] || {}; return { kind: "twitch", login, id: user.id || "", displayName: user.displayName || login, color: null, image: user.image || null, missing: !!user.missing }; }),
  ].map((bot, index) => ({ ...bot, index })).sort((a, b) => (b.kind === "custom") - (a.kind === "custom") || autoJoinLogins.has(b.login) - autoJoinLogins.has(a.login) || a.index - b.index);
  const autoBoostOf = (login) => botOptions[login]?.autoBoost !== false;
  // What a bot becomes on the auto-join list / in a POST /join body.
  const joinEntryOf = (bot) => ({ id: bot.id || "", login: bot.login, displayName: bot.displayName, color: bot.color || null, sub: false, image: bot.kind === "custom" ? bot.image : null, autoBoost: autoBoostOf(bot.login) });

  const toggleAutoJoin = (bot) => { const list = settings.autoJoin || []; saveSettings({ autoJoin: autoJoinLogins.has(bot.login) ? list.filter((entry) => entry.login !== bot.login) : [...list, joinEntryOf(bot)] }); };
  const toggleAutoBoost = (bot) => saveSettings({ botOptions: { ...botOptions, [bot.login]: { autoBoost: !autoBoostOf(bot.login) } } });
  const removeBot = (bot) => saveSettings({
    bots: bot.kind === "twitch" ? twitchLogins.filter((login) => login !== bot.login) : twitchLogins,
    customBots: bot.kind === "custom" ? customBots.filter((customBot) => customBot.login !== bot.login) : customBots,
    autoJoin: (settings.autoJoin || []).filter((entry) => entry.login !== bot.login),
  });
  const addTwitchBots = (users) => saveSettings({ bots: [...new Set([...twitchLogins, ...users.map((user) => user.login)])] });
  const addCustomBot = (bot) => saveSettings({ customBots: [...customBots.filter((customBot) => customBot.login !== bot.login), bot] });
  const replacePicture = async (bot, file) => {
    const upload = await api(`/image/${bot.login}`, { method: "PUT", body: { data: await readFileAsDataUrl(file) } });
    if (upload?.path) addCustomBot({ ...customBots.find((customBot) => customBot.login === bot.login), image: upload.path });
  };
  const joinNow = (bot) => api("/join", { body: [joinEntryOf(bot)] });
  const togglePick = (login) => setPickedLogins((previous) => { const next = new Set(previous); next.has(login) ? next.delete(login) : next.add(login); return next; });
  const pickedEntries = roster.filter((bot) => pickedLogins.has(bot.login) && !bot.missing).map(joinEntryOf);
  const autoJoinCount = roster.filter((bot) => autoJoinLogins.has(bot.login)).length;

  return html`
    <div class="bots">
      <article>
        <header>Bots <span class="hint-inline">race as AI cars · ★ auto-join every lobby · ↯ auto-boost (off = something else drives its pool via the API)</span></header>
        <${TwitchLookup} onAdd=${addTwitchBots} label="Add Twitch" />
        <${AddCustomBotForm} onAdd=${addCustomBot} />
      </article>

      <article class="people">
        <header>
          <span>Roster <b>· ${roster.length}</b> <span class="hint-inline">${autoJoinCount} auto-join</span></span>
          <div class="toolbar">
            ${pickedLogins.size ? html`<span class="count">${pickedLogins.size} selected</span>` : null}
            <button onClick=${() => setPickedLogins(new Set(roster.filter((bot) => !bot.missing).map((bot) => bot.login)))}>All</button>
            <button onClick=${() => setPickedLogins(new Set())} disabled=${!pickedLogins.size}>None</button>
            <button class="go" disabled=${!pickedEntries.length || snapshot.running} onClick=${() => api("/join", { body: pickedEntries })}>Add selected to lobby</button>
            <button onClick=${() => confirm("Restore the built-in Twitch list?") && saveSettings({ bots: null })} title="restore the built-in Twitch list">Defaults</button>
          </div>
        </header>
        ${roster.length ? html`
          <${Grid}>
            ${roster.map((bot) => html`
              <${BotCard} key=${bot.login} bot=${bot} picked=${pickedLogins.has(bot.login)} inLobby=${lobbyLogins.has(bot.login)} racing=${snapshot.running}
                autoJoin=${autoJoinLogins.has(bot.login)} autoBoost=${autoBoostOf(bot.login)}
                onTogglePick=${togglePick} onToggleAutoJoin=${toggleAutoJoin} onToggleAutoBoost=${toggleAutoBoost} onRemove=${removeBot} onReplacePicture=${replacePicture} onJoin=${joinNow} />`)}
          <//>`
        : html`<${Empty} text="Roster is empty" />`}
      </article>
    </div>`;
}
