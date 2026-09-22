// The live /events viewer on the API page: its own EventSource (opened on demand), the last 60 events, a name filter.
import { useState, useRef, useEffect } from "react";
import { html } from "../../lib/html.js";
import { withToken } from "../../lib/api.js";

const KEEP = 60;   // entries remembered
const SHOWN = 12;  // entries drawn
const DATA_PREVIEW = 300; // chars of payload per line

export default function EventLog({ events }) {
  const [entries, setEntries] = useState([]);
  const [listening, setListening] = useState(false);
  const [filter, setFilter] = useState("");
  const eventSourceRef = useRef(null);
  useEffect(() => () => eventSourceRef.current?.close(), []);
  const log = (eventName, data) => setEntries((previous) => [{ time: new Date().toLocaleTimeString(), eventName, data }, ...previous].slice(0, KEEP));
  const toggle = () => {
    if (listening) { eventSourceRef.current?.close(); eventSourceRef.current = null; setListening(false); return; }
    const eventSource = new EventSource(withToken("/events"));
    for (const [eventName] of events) eventSource.addEventListener(eventName, (event) => log(eventName, event.data));
    eventSource.onerror = () => log("error", "connection lost (token? game closed?)");
    eventSourceRef.current = eventSource; setListening(true);
  };
  const shown = entries.filter((entry) => !filter || entry.eventName === filter);
  return html`
    <div class="evlog">
      <div class="res-head">
        <button class=${listening ? "" : "secondary"} onClick=${toggle}>${listening ? "Disconnect" : "Listen to /events"}</button>
        <select value=${filter} onChange=${(event) => setFilter(event.target.value)}><option value="">all events</option>${events.map(([name]) => html`<option key=${name} value=${name}>${name}</option>`)}</select>
        ${entries.length ? html`<button class="secondary outline" onClick=${() => setEntries([])}>Clear</button>` : null}
      </div>
      ${shown.slice(0, SHOWN).map((entry, index) => html`<div class="ev" key=${index}><span class="t">${entry.time}</span><span class="name">${entry.eventName}</span><span class="d">${entry.data.length > DATA_PREVIEW ? entry.data.slice(0, DATA_PREVIEW) + " …" : entry.data}</span></div>`)}
    </div>`;
}
