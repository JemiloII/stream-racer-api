// The live timing board shared by the Controls and Camera pages: one row per car from the race snapshot with the
// per-racer quick actions (chase cam, boom, boost, +boost, slow, respawn, finish, kick) and the auto-join star.
import { html, useCss } from "../lib/html.js";
import { api } from "../lib/api.js";
import { useStore } from "../store.js";

function RacerRow({ racer, streamer, uiSettings, starred, onToggleStar }) {
  const target = encodeURIComponent(racer.login);
  return html`
    <div class="row-r">
      <div class="pos">${racer.place}</div>
      <button class=${"star" + (starred ? " on" : "")} title=${starred ? "on the auto-join list (click to remove)" : "add to the auto-join list"} onClick=${() => onToggleStar(racer)}>★</button>
      <div class="who">
        <span class="swatch" style=${{ background: racer.color, color: racer.color }}></span>
        <div>
          <div class="nm">${racer.displayName}${racer.login === streamer ? html`<span class="you">YOU</span>` : null}</div>
          <div class="lg">${racer.login}${racer.sub ? " · sub" : ""}</div>
        </div>
      </div>
      <div class=${"track" + (racer.finished ? " done" : "")}><i style=${{ width: (racer.finished ? 100 : racer.pct).toFixed(1) + "%" }}></i></div>
      <div class=${"pct" + (racer.finished ? " done" : "")}>${racer.finished ? "FIN" : racer.pct.toFixed(0) + "%"}</div>
      <div class="quick">
        <button class="c" title="chase cam on them" onClick=${() => api(`/camera/chase/${target}`)}>🎥</button>
        <button class="b" title="boom" onClick=${() => api(`/boom/${target}`)}>💥</button>
        <button class="z" title="boost now" onClick=${() => api(`/boost/${target}`)}>⚡</button>
        <button class="p" title="+1 boost to their pool" onClick=${() => api(`/boost/${target}/add`)}><span class="plus">+</span>⚡</button>
        <button class="s" title="slow" onClick=${() => api(`/speed/${target}?mult=${uiSettings.slowMult}&seconds=${uiSettings.slowSecs}`)}>🐌</button>
        <button class="r" title="respawn" onClick=${() => api(`/respawn/${target}`)}>🏥</button>
        <button class="f" title="mark as finished (crossed the line but the game missed it)" onClick=${() => confirm(`Mark ${racer.displayName} as finished?`) && api(`/finish/${target}`)}>🏁</button>
        <button class="k" title="kick (lobby only)" onClick=${() => api(`/kick/${target}`)}>✕</button>
        <span class="boosts" title="boosts left"><b>${racer.boosts}</b></span>
      </div>
    </div>`;
}

// The auto-join entry a racer becomes when starred (same shape as POST /join).
const autoJoinEntryOf = (racer) => ({ id: racer.id || "", login: racer.login, displayName: racer.displayName, color: racer.color, sub: !!racer.sub, image: racer.image || null });

export default function Racers() {
  useCss("components/racers.css");
  const { snapshot, search, setSearch, settings, saveSettings } = useStore();
  const uiSettings = useStore((state) => state.uiSettings());
  const autoJoinList = settings.autoJoin || [];
  const starredLogins = new Set(autoJoinList.map((entry) => entry.login));
  const toggleStar = (racer) => saveSettings({ autoJoin: starredLogins.has(racer.login) ? autoJoinList.filter((entry) => entry.login !== racer.login) : [...autoJoinList, autoJoinEntryOf(racer)] });
  const query = search.trim().toLowerCase();
  const rows = query ? snapshot.vehicles.filter((racer) => racer.login.includes(query) || racer.displayName.toLowerCase().includes(query)) : snapshot.vehicles;
  return html`
    <article class="board">
      <header class="board-head">
        <span>Timing ${snapshot.vehicles.length ? html`<b>· ${snapshot.vehicles.length}</b>` : null}</span>
        <input type="search" placeholder="search racers" value=${search} onInput=${(event) => setSearch(event.target.value)} />
      </header>
      ${rows.length
        ? rows.map((racer) => html`<${RacerRow} key=${racer.login} racer=${racer} streamer=${snapshot.streamer} uiSettings=${uiSettings} starred=${starredLogins.has(racer.login)} onToggleStar=${toggleStar} />`)
        : html`<div class="board-empty">${snapshot.vehicles.length ? "No match" : "No cars on track"}</div>`}
    </article>`;
}
