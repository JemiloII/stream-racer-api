import { useState } from "react";
import { html, useStore, useCss, api } from "../store.js";
import { TwitchLookup, Card, Grid, Empty, useTwitchUsers, readFile } from "../components/roster.js";

// One roster: Twitch bots (resolved live) and custom bots (your own login + picture) side by side.
// Card tools: ★ auto-join every lobby · ↯ auto-boost (off = a third party drives its pool via the API) · ✕ remove.
// Badges say what's on. Custom and auto-join bots sort first.

function AddCustom({ onAdd }) {
  const [login, setLogin] = useState(""), [name, setName] = useState(""), [color, setColor] = useState("#35e0ff"), [autoColor, setAutoColor] = useState(true), [image, setImage] = useState(""), [file, setFile] = useState(null), [busy, setBusy] = useState(false);
  const add = async () => {
    const l = login.trim().toLowerCase().replace(/[^a-z0-9_]/g, ""); if (!l) return;
    setBusy(true);
    let img = image.trim();
    if (file) { const r = await api(`/image/${l}`, { method: "PUT", body: { data: await readFile(file) } }); if (r?.path) img = r.path; }
    await onAdd({ id: "", login: l, displayName: name.trim() || login.trim(), color: autoColor ? null : color, image: img || null });
    setLogin(""); setName(""); setImage(""); setFile(null); setBusy(false);
  };
  return html`
    <div class="cbform">
      <label>Custom bot login<input placeholder="my_bot" value=${login} onInput=${(e) => setLogin(e.target.value)} /></label>
      <label>Display name<input placeholder="My Bot" value=${name} onInput=${(e) => setName(e.target.value)} /></label>
      <label>Color
        <div class="colorrow">
          <button class=${"secondary" + (autoColor ? " on" : "")} onClick=${() => setAutoColor(!autoColor)} title="pick one automatically">auto</button>
          <input type="color" value=${color} disabled=${autoColor} onInput=${(e) => setColor(e.target.value)} />
        </div>
      </label>
      <label>Picture
        <label class="filebtn"><span>${file ? file.name : "Choose file…"}</span><input type="file" accept="image/*" hidden onChange=${(e) => setFile(e.target.files[0] || null)} /></label>
      </label>
      <label>…or image URL / path<input placeholder="https://… or C:/…/bot.png" value=${image} onInput=${(e) => setImage(e.target.value)} /></label>
      <button disabled=${busy || !login.trim()} aria-busy=${busy} onClick=${add}>Add custom</button>
    </div>`;
}

export default function Bots() {
  useCss("pages/bots.css");
  const { snap, settings, saveSettings } = useStore();
  const logins = settings.bots || [];
  const custom = settings.customBots || [];
  const opts = settings.botOptions || {};
  const ajLogins = new Set((settings.autoJoin || []).map((e) => e.login));
  const inLobby = new Set(snap.vehicles.map((v) => v.login));
  const users = useTwitchUsers(logins);
  const [picked, setPicked] = useState(new Set());

  const roster = [
    ...custom.map((b) => ({ kind: "custom", ...b })),
    ...logins.map((l) => { const u = users[l] || {}; return { kind: "twitch", login: l, id: u.id || "", displayName: u.displayName || l, color: null, image: u.image || null, missing: !!u.missing }; }),
  ].map((r, i) => ({ ...r, i })).sort((a, b) => (b.kind === "custom") - (a.kind === "custom") || ajLogins.has(b.login) - ajLogins.has(a.login) || a.i - b.i);
  const autoBoost = (login) => opts[login]?.autoBoost !== false;
  const entryOf = (r) => ({ id: r.id || "", login: r.login, displayName: r.displayName, color: r.color || null, sub: false, image: r.kind === "custom" ? r.image : null, autoBoost: autoBoost(r.login) });

  const toggleAutoJoin = (r) => { const list = settings.autoJoin || []; saveSettings({ autoJoin: ajLogins.has(r.login) ? list.filter((e) => e.login !== r.login) : [...list, entryOf(r)] }); };
  const toggleAutoBoost = (r) => saveSettings({ botOptions: { ...opts, [r.login]: { autoBoost: !autoBoost(r.login) } } });
  const remove = (r) => saveSettings({
    bots: r.kind === "twitch" ? logins.filter((l) => l !== r.login) : logins,
    customBots: r.kind === "custom" ? custom.filter((b) => b.login !== r.login) : custom,
    autoJoin: (settings.autoJoin || []).filter((e) => e.login !== r.login),
  });
  const addTwitch = (us) => saveSettings({ bots: [...new Set([...logins, ...us.map((u) => u.login)])] });
  const addCustom = (bot) => saveSettings({ customBots: [...custom.filter((b) => b.login !== bot.login), bot] });
  const replacePicture = async (r, file) => { const res = await api(`/image/${r.login}`, { method: "PUT", body: { data: await readFile(file) } }); if (res?.path) addCustom({ ...custom.find((b) => b.login === r.login), image: res.path }); };
  const togglePick = (login) => setPicked((p) => { const n = new Set(p); n.has(login) ? n.delete(login) : n.add(login); return n; });
  const pickedEntries = roster.filter((r) => picked.has(r.login) && !r.missing).map(entryOf);
  const autoCount = roster.filter((r) => ajLogins.has(r.login)).length;

  return html`
    <div class="bots">
      <article>
        <header>Bots <span class="hint-inline">race as AI cars · ★ auto-join every lobby · ↯ auto-boost (off = something else drives its pool via the API)</span></header>
        <${TwitchLookup} onAdd=${addTwitch} label="Add Twitch" />
        <${AddCustom} onAdd=${addCustom} />
      </article>

      <article class="people">
        <header>
          <span>Roster <b>· ${roster.length}</b> <span class="hint-inline">${autoCount} auto-join</span></span>
          <div class="toolbar">
            ${picked.size ? html`<span class="count">${picked.size} selected</span>` : null}
            <button onClick=${() => setPicked(new Set(roster.filter((r) => !r.missing).map((r) => r.login)))}>All</button>
            <button onClick=${() => setPicked(new Set())} disabled=${!picked.size}>None</button>
            <button class="go" disabled=${!pickedEntries.length || snap.running} onClick=${() => api("/join", { body: pickedEntries })}>Add selected to lobby</button>
            <button onClick=${() => confirm("Restore the built-in Twitch list?") && saveSettings({ bots: null })} title="restore the built-in Twitch list">Defaults</button>
          </div>
        </header>
        ${roster.length ? html`
          <${Grid}>
            ${roster.map((r) => html`
              <${Card} key=${r.login} r=${r} picked=${picked.has(r.login)} inLobby=${inLobby.has(r.login)} onClick=${() => !r.missing && togglePick(r.login)}
                badges=${[...(ajLogins.has(r.login) ? ["Auto-join"] : []), ...(autoBoost(r.login) ? [] : ["API boosts"])]}
                tools=${html`
                  <button class=${"star" + (ajLogins.has(r.login) ? " on" : "")} title=${ajLogins.has(r.login) ? "auto-joins every lobby (click to stop)" : "auto-join every lobby"} onClick=${(e) => { e.stopPropagation(); toggleAutoJoin(r); }}>★</button>
                  <button class=${"bolt" + (autoBoost(r.login) ? " on" : "")} title=${autoBoost(r.login) ? "auto-boost on (click: let something else control it)" : "auto-boost off: a third party drives this car's boosts"} onClick=${(e) => { e.stopPropagation(); toggleAutoBoost(r); }}>↯</button>
                  <span class="sp"></span>
                  <button class="x" title="remove" onClick=${(e) => { e.stopPropagation(); remove(r); }}>✕</button>`}
                picture=${r.kind === "custom" ? html`<label class="pic-wrap" onClick=${(e) => e.stopPropagation()} title="click to change picture">
                    ${r.image ? html`<img src=${"/image/" + r.login + "?v=" + encodeURIComponent(r.image)} alt="" style=${r.color ? { borderColor: r.color } : {}} />` : html`<div class="ph" style=${r.color ? { background: r.color, color: "#000" } : {}}>${(r.displayName || r.login).slice(0, 2).toUpperCase()}</div>`}
                    <input type="file" accept="image/*" hidden onClick=${(e) => e.stopPropagation()} onChange=${(e) => e.target.files[0] && replacePicture(r, e.target.files[0])} />
                  </label>` : undefined}>
                ${!r.missing ? html`<button class="join" disabled=${snap.running || inLobby.has(r.login)} onClick=${(e) => { e.stopPropagation(); api("/join", { body: [entryOf(r)] }); }}>${inLobby.has(r.login) ? "in lobby" : "Join now"}</button>` : null}
              <//>`)}
          <//>`
        : html`<${Empty} text="Roster is empty" />`}
      </article>
    </div>`;
}
