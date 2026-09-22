// One bot on the roster: the shared person Card with the ★ / ↯ / ✕ tools, the Auto-join / API boosts badges,
// a click-to-replace picture for custom bots and the "Join now" button.
import { html } from "../../lib/html.js";
import { initialsOf } from "../../lib/text.js";
import { Card } from "../../components/roster.js";

const stopPropagation = (event) => event.stopPropagation();

// Custom bots keep their picture editable: clicking it opens the file picker and uploads a replacement.
function EditablePicture({ bot, onReplace }) {
  return html`<label class="pic-wrap" onClick=${stopPropagation} title="click to change picture">
      ${bot.image ? html`<img src=${"/image/" + bot.login + "?v=" + encodeURIComponent(bot.image)} alt="" style=${bot.color ? { borderColor: bot.color } : {}} />` : html`<div class="ph" style=${bot.color ? { background: bot.color, color: "#000" } : {}}>${initialsOf(bot)}</div>`}
      <input type="file" accept="image/*" hidden onClick=${stopPropagation} onChange=${(event) => event.target.files[0] && onReplace(bot, event.target.files[0])} />
    </label>`;
}

export default function BotCard({ bot, picked, inLobby, racing, autoJoin, autoBoost, onTogglePick, onToggleAutoJoin, onToggleAutoBoost, onRemove, onReplacePicture, onJoin }) {
  const badges = [...(autoJoin ? ["Auto-join"] : []), ...(autoBoost ? [] : ["API boosts"])];
  const tools = html`
    <button class=${"star" + (autoJoin ? " on" : "")} title=${autoJoin ? "auto-joins every lobby (click to stop)" : "auto-join every lobby"} onClick=${(event) => { event.stopPropagation(); onToggleAutoJoin(bot); }}>★</button>
    <button class=${"bolt" + (autoBoost ? " on" : "")} title=${autoBoost ? "auto-boost on (click: let something else control it)" : "auto-boost off: a third party drives this car's boosts"} onClick=${(event) => { event.stopPropagation(); onToggleAutoBoost(bot); }}>↯</button>
    <span class="sp"></span>
    <button class="x" title="remove" onClick=${(event) => { event.stopPropagation(); onRemove(bot); }}>✕</button>`;
  return html`
    <${Card} person=${bot} picked=${picked} inLobby=${inLobby} onClick=${() => !bot.missing && onTogglePick(bot.login)} badges=${badges} tools=${tools}
      picture=${bot.kind === "custom" ? html`<${EditablePicture} bot=${bot} onReplace=${onReplacePicture} />` : undefined}>
      ${!bot.missing ? html`<button class="join" disabled=${racing || inLobby} onClick=${(event) => { event.stopPropagation(); onJoin(bot); }}>${inLobby ? "in lobby" : "Join now"}</button>` : null}
    <//>`;
}
