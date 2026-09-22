// Shared people pieces: a Twitch lookup box (logins / links → resolved users), the person card, the card grid and
// the empty placeholder. Used by the Bots page and the auto-join list on Settings.
import { useState, useEffect } from "react";
import { html, useCss } from "../lib/html.js";
import { api } from "../lib/api.js";
import { initialsOf } from "../lib/text.js";

// Resolve Twitch logins to {id, login, displayName, image}; cached for the page's life. Unknown logins get {missing: true}.
const userCache = {};
export function useTwitchUsers(logins) {
  const [, rerender] = useState(0);
  const unresolved = logins.filter((login) => !userCache[login]);
  useEffect(() => {
    if (!unresolved.length) return;
    for (const login of unresolved) userCache[login] = { login, pending: true };
    api(`/twitch/users?logins=${encodeURIComponent(unresolved.join(","))}`, { method: "GET" }).then((response) => {
      for (const login of unresolved) userCache[login] = { login, missing: true };
      for (const user of response?.users || []) userCache[user.login] = user;
      rerender((count) => count + 1);
    });
  }, [unresolved.join(",")]);
  return userCache;
}

// "@Name", "twitch.tv/name" or "name" → "name"
const loginOf = (text) => text.trim().toLowerCase().replace(/^@/, "").replace(/^https?:\/\/(www\.)?twitch\.tv\//, "");

// Text box: paste logins or twitch.tv links, Enter/Add resolves them and hands back the users found.
export function TwitchLookup({ onAdd, placeholder = "Twitch logins or twitch.tv links, comma separated", label = "Add" }) {
  useCss("components/roster.css");
  const [text, setText] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const add = async () => {
    const wanted = [...new Set(text.split(/[\s,]+/).map(loginOf).filter(Boolean))];
    if (!wanted.length) return;
    setBusy(true);
    const response = await api(`/twitch/users?logins=${encodeURIComponent(wanted.join(","))}`, { method: "GET" });
    setBusy(false);
    if (!response) return;
    for (const user of response.users) userCache[user.login] = user;
    const unknown = wanted.filter((login) => !response.users.some((user) => user.login === login));
    setMessage(unknown.length ? `Not on Twitch: ${unknown.join(", ")}` : "");
    if (response.users.length) { await onAdd(response.users); setText(""); }
  };
  return html`
    <div class="lookup">
      <input placeholder=${placeholder} value=${text} onInput=${(event) => setText(event.target.value)} onKeyDown=${(event) => event.key === "Enter" && add()} spellCheck="false" />
      <button onClick=${add} disabled=${busy || !text.trim()} aria-busy=${busy}>${label}</button>
      ${message ? html`<p class="lookup-msg">${message}</p>` : null}
    </div>`;
}

// One person. person: {login, displayName, color?, image?, kind: "twitch"|"custom", missing?}.
// Slots: `tools` (top-left buttons), `badges` (labels under them), `picture` (replaces the avatar), children (bottom).
export function Card({ person, picked, inLobby, onClick, tools, badges, children, picture }) {
  useCss("components/roster.css");
  const shownPicture = picture ?? (person.image
    ? html`<img src=${person.kind === "custom" ? "/image/" + person.login + "?v=" + encodeURIComponent(person.image) : person.image} alt="" loading="lazy" style=${person.color ? { borderColor: person.color } : {}} />`
    : html`<div class="ph" style=${person.color ? { background: person.color, color: "#000" } : {}}>${initialsOf(person)}</div>`);
  return html`
    <div class=${"card" + (picked ? " on" : "") + (inLobby ? " in" : "") + (person.missing ? " bad" : "")} onClick=${onClick}>
      <div class="tools">${tools}</div>
      <div class="badges">${(badges || []).map((badge) => html`<span key=${badge} class=${"badge " + badge.toLowerCase().replace(/\W+/g, "-")}>${badge}</span>`)}</div>
      ${shownPicture}
      <div class="nm" style=${person.kind === "custom" && person.color ? { color: person.color } : {}}>${person.displayName || person.login}</div>
      <div class="lg">${person.missing ? "not on Twitch" : (person.kind === "custom" ? "custom · " : "@") + person.login}${inLobby ? " · in lobby" : ""}</div>
      ${children}
    </div>`;
}

export const Grid = ({ children }) => html`<div class="cards">${children}</div>`;
export const Empty = ({ text }) => html`<div class="empty">${text}</div>`;
