import { useState, useEffect } from "react";
import { html, useCss, api } from "../store.js";

// Shared people pieces: a Twitch lookup box (logins/links -> resolved users) and the person card.
// Used by the Bots page and the auto-join list on Settings.

export const readFile = (file) => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(file); });

// Resolve Twitch logins to {id, login, displayName, image}; cached for the page's life. Unknown logins get {missing: true}.
const cache = {};
export function useTwitchUsers(logins) {
  const [, bump] = useState(0);
  const need = logins.filter((l) => !cache[l]);
  useEffect(() => {
    if (!need.length) return;
    for (const l of need) cache[l] = { login: l, pending: true };
    api(`/twitch/users?logins=${encodeURIComponent(need.join(","))}`, { method: "GET" }).then((r) => {
      for (const l of need) cache[l] = { login: l, missing: true };
      for (const u of r?.users || []) cache[u.login] = u;
      bump((n) => n + 1);
    });
  }, [need.join(",")]);
  return cache;
}

// Text box: paste logins or twitch.tv links, Enter/Add resolves them and hands back the users found.
export function TwitchLookup({ onAdd, placeholder = "Twitch logins or twitch.tv links, comma separated", label = "Add" }) {
  useCss("components/roster.css");
  const [text, setText] = useState(""), [busy, setBusy] = useState(false), [msg, setMsg] = useState("");
  const add = async () => {
    const want = [...new Set(text.split(/[\s,]+/).map((s) => s.trim().toLowerCase().replace(/^@/, "").replace(/^https?:\/\/(www\.)?twitch\.tv\//, "")).filter(Boolean))];
    if (!want.length) return;
    setBusy(true);
    const r = await api(`/twitch/users?logins=${encodeURIComponent(want.join(","))}`, { method: "GET" });
    setBusy(false);
    if (!r) return;
    for (const u of r.users) cache[u.login] = u;
    const bad = want.filter((l) => !r.users.some((u) => u.login === l));
    setMsg(bad.length ? `Not on Twitch: ${bad.join(", ")}` : "");
    if (r.users.length) { await onAdd(r.users); setText(""); }
  };
  return html`
    <div class="lookup">
      <input placeholder=${placeholder} value=${text} onInput=${(e) => setText(e.target.value)} onKeyDown=${(e) => e.key === "Enter" && add()} spellCheck="false" />
      <button onClick=${add} disabled=${busy || !text.trim()} aria-busy=${busy}>${label}</button>
      ${msg ? html`<p class="lookup-msg">${msg}</p>` : null}
    </div>`;
}

// One person. r: {login, displayName, color?, image?, kind: "twitch"|"custom", missing?}. Slots: top-left buttons, badges, bottom.
export function Card({ r, picked, inLobby, onClick, tools, badges, children, picture }) {
  useCss("components/roster.css");
  const initials = (r.displayName || r.login || "?").slice(0, 2).toUpperCase();
  const pic = picture ?? (r.image
    ? html`<img src=${r.kind === "custom" ? "/image/" + r.login + "?v=" + encodeURIComponent(r.image) : r.image} alt="" loading="lazy" style=${r.color ? { borderColor: r.color } : {}} />`
    : html`<div class="ph" style=${r.color ? { background: r.color, color: "#000" } : {}}>${initials}</div>`);
  return html`
    <div class=${"card" + (picked ? " on" : "") + (inLobby ? " in" : "") + (r.missing ? " bad" : "")} onClick=${onClick}>
      <div class="tools">${tools}</div>
      <div class="badges">${(badges || []).map((b) => html`<span key=${b} class=${"badge " + b.toLowerCase().replace(/\W+/g, "-")}>${b}</span>`)}</div>
      ${pic}
      <div class="nm" style=${r.kind === "custom" && r.color ? { color: r.color } : {}}>${r.displayName || r.login}</div>
      <div class="lg">${r.missing ? "not on Twitch" : (r.kind === "custom" ? "custom · " : "@") + r.login}${inLobby ? " · in lobby" : ""}</div>
      ${children}
    </div>`;
}

export const Grid = ({ children }) => html`<div class="cards">${children}</div>`;
export const Empty = ({ text }) => html`<div class="empty">${text}</div>`;
