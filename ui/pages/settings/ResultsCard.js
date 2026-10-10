// Settings → Race results: whether results go to the game's servers (season points), and links to the local
// standings browser source (/rankings), which works either way.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";

const PERIODS = [["day", "Today"], ["week", "This week"], ["month", "This month"], ["all", "All time"]];

export default function ResultsCard() {
  const { settings, saveSettings } = useStore();
  const [period, setPeriod] = useState("week"), [copied, setCopied] = useState(false);
  const url = `${location.origin}/rankings?period=${period}&rows=10`;
  const copy = () => navigator.clipboard.writeText(url).then(() => {
    setCopied(true);
    setTimeout(() => setCopied(false), 1200);
  });
  return html`
    <article>
      <header>Race results</header>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${settings.sendResults !== false} onChange=${(event) => saveSettings({ sendResults: event.target.checked })} />
        <span>Ranked ${settings.sendResults !== false ? "on" : "off"}: send race results to the game's servers (season points)</span>
      </label>
      <p class="hint">Off: results stay on this PC and nobody gets season points. Every race is saved here either way (BepInEx/config/shibiko.streamracer.results.jsonl).</p>
      <p class="hint">Local standings browser source: 1st of 8 scores 8, the last finisher 1, did not finish 0.</p>
      <div class="mmlink">
        <label>Period<select value=${period} onChange=${(event) => setPeriod(event.target.value)}>${PERIODS.map(([value, label]) => html`<option key=${value} value=${value}>${label}</option>`)}</select></label>
        <input readOnly value=${url} onFocus=${(event) => event.target.select()} />
        <button class="secondary" onClick=${copy}>${copied ? "Copied" : "Copy link"}</button>
      </div>
    </article>`;
}
