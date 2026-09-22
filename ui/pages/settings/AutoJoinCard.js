// Settings → Auto-join list: the Twitch viewers who join every lobby. Bots are starred on the Bots page instead,
// so they are counted but not listed here.
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { useStore } from "../../store.js";
import { TwitchLookup, Card, Grid, Empty, useTwitchUsers } from "../../components/roster.js";

export default function AutoJoinCard() {
  const { settings, saveSettings, snapshot } = useStore();
  const autoJoinList = settings.autoJoin || [];
  const botLogins = new Set([...(settings.bots || []), ...(settings.customBots || []).map((bot) => bot.login)]);
  const people = autoJoinList.filter((entry) => !botLogins.has(entry.login));
  const botsStarred = autoJoinList.length - people.length;
  const twitchUsers = useTwitchUsers(people.filter((entry) => !entry.image).map((entry) => entry.login));
  const avatars = Object.fromEntries(Object.entries(twitchUsers).map(([login, user]) => [login, user.image]));

  const addEntries = (entries) => saveSettings({ autoJoin: [...autoJoinList, ...entries.filter((entry) => !autoJoinList.some((existing) => existing.login === entry.login))] });
  const addEveryoneInLobby = () => addEntries(snapshot.vehicles.map((racer) => ({ login: racer.login, id: racer.id || "", displayName: racer.displayName, color: racer.color, sub: !!racer.sub, image: racer.image || null })));
  const addTwitchUsers = (users) => addEntries(users.map((user) => ({ login: user.login, id: user.id, displayName: user.displayName, color: null, sub: false, image: null })));
  const removeEntry = (entry) => saveSettings({ autoJoin: autoJoinList.filter((existing) => existing.login !== entry.login) });
  const clearPeople = () => confirm("Remove every viewer from the auto-join list? (bots keep their ★)") && saveSettings({ autoJoin: autoJoinList.filter((entry) => botLogins.has(entry.login)) });

  return html`
    <article>
      <header>Auto-join list <span class="hint-inline">${people.length} people join every lobby</span></header>
      <p class="hint">Twitch viewers you want in every race, looked up here. Bots are handled on the Bots page (★ there = auto-join), so they don't show in this list.${botsStarred ? ` ${botsStarred} bot${botsStarred > 1 ? "s" : ""} auto-join from there.` : ""}</p>
      <${TwitchLookup} onAdd=${addTwitchUsers} label="Add" />
      <div class="toolbar" style=${{ margin: "10px 0 12px" }}>
        <button class="go" disabled=${!autoJoinList.length || snapshot.running} onClick=${() => api("/autojoin/join")}>Join them all now</button>
        <button disabled=${!snapshot.vehicles.length} onClick=${addEveryoneInLobby}>Add everyone in the lobby</button>
        <button disabled=${!people.length} onClick=${clearPeople}>Clear</button>
      </div>
      ${people.length ? html`<${Grid}>
        ${people.map((entry) => html`<${Card} key=${entry.login} person=${{ ...entry, kind: "twitch", image: entry.image || avatars[entry.login] || null }}
          badges=${["Auto-join"]}
          tools=${html`<span class="sp"></span><button class="x" title="remove" onClick=${() => removeEntry(entry)}>✕</button>`} />`)}
      <//>` : html`<${Empty} text="No viewers on the list" />`}
    </article>`;
}
