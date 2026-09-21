import { html, useStore, useCss, api } from "../store.js";

const enc = encodeURIComponent;

function Row({ r, streamer, ui, starred, toggleStar }) {
  const L = enc(r.login);
  return html`
    <div class="row-r">
      <div class="pos">${r.place}</div>
      <button class=${"star" + (starred ? " on" : "")} title=${starred ? "on the auto-join list (click to remove)" : "add to the auto-join list"} onClick=${() => toggleStar(r)}>★</button>
      <div class="who">
        <span class="swatch" style=${{ background: r.color, color: r.color }}></span>
        <div>
          <div class="nm">${r.displayName}${r.login === streamer ? html`<span class="you">YOU</span>` : null}</div>
          <div class="lg">${r.login}${r.sub ? " · sub" : ""}</div>
        </div>
      </div>
      <div class=${"track" + (r.finished ? " done" : "")}><i style=${{ width: (r.finished ? 100 : r.pct).toFixed(1) + "%" }}></i></div>
      <div class=${"pct" + (r.finished ? " done" : "")}>${r.finished ? "FIN" : r.pct.toFixed(0) + "%"}</div>
      <div class="quick">
        <button class="c" title="chase cam on them" onClick=${() => api(`/camera/chase/${L}`)}>🎥</button>
        <button class="b" title="boom" onClick=${() => api(`/boom/${L}`)}>💥</button>
        <button class="z" title="boost now" onClick=${() => api(`/boost/${L}`)}>⚡</button>
        <button class="p" title="+1 boost to their pool" onClick=${() => api(`/boost/${L}/add`)}><span class="plus">+</span>⚡</button>
        <button class="s" title="slow" onClick=${() => api(`/speed/${L}?mult=${ui.slowMult}&seconds=${ui.slowSecs}`)}>🐌</button>
        <button class="r" title="respawn" onClick=${() => api(`/respawn/${L}`)}>🏥</button>
        <button class="f" title="mark as finished (crossed the line but the game missed it)" onClick=${() => confirm(`Mark ${r.displayName} as finished?`) && api(`/finish/${L}`)}>🏁</button>
        <button class="k" title="kick (lobby only)" onClick=${() => api(`/kick/${L}`)}>✕</button>
        <span class="boosts" title="boosts left"><b>${r.boosts}</b></span>
      </div>
    </div>`;
}

export default function Racers() {
  useCss("components/racers.css");
  const { snap, search, setSearch, settings, saveSettings } = useStore();
  const aj = settings.autoJoin || [];
  const starred = new Set(aj.map((e) => e.login));
  const toggleStar = (r) => saveSettings({ autoJoin: starred.has(r.login) ? aj.filter((e) => e.login !== r.login) : [...aj, { id: r.id || "", login: r.login, displayName: r.displayName, color: r.color, sub: !!r.sub, image: r.image || null }] });
  const ui = useStore((s) => s.ui());
  const q = search.trim().toLowerCase();
  const rows = q ? snap.vehicles.filter((r) => r.login.includes(q) || r.displayName.toLowerCase().includes(q)) : snap.vehicles;
  return html`
    <article class="board">
      <header class="board-head">
        <span>Timing ${snap.vehicles.length ? html`<b>· ${snap.vehicles.length}</b>` : null}</span>
        <input type="search" placeholder="search racers" value=${search} onInput=${(e) => setSearch(e.target.value)} />
      </header>
      ${rows.length
        ? rows.map((r) => html`<${Row} key=${r.login} r=${r} streamer=${snap.streamer} ui=${ui} starred=${starred.has(r.login)} toggleStar=${toggleStar} />`)
        : html`<div class="board-empty">${snap.vehicles.length ? "No match" : "No cars on track"}</div>`}
    </article>`;
}
