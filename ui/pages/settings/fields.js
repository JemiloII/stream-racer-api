// Form fields the Settings cards share. Both live at module level on purpose: pages re-render 60×/s during a race,
// and a component defined inside a render would remount the input and wipe what is being typed (see CLAUDE.md).
import { html } from "../../lib/html.js";

// Number input bound to values[name]; commits on blur / Enter, only when the value changed.
export function NumberField({ values, name, label, step, onChange }) {
  const commit = (event) => { const value = +event.target.value; if (!Number.isNaN(value) && value !== values[name]) onChange(name, value); };
  return html`<label>${label}<input type="number" step=${step || 0.01} defaultValue=${values[name]} onBlur=${commit} onKeyDown=${(event) => event.key === "Enter" && event.target.blur()} /></label>`;
}

// Select bound to values[name]; options = [[value, label], …].
export const SelectField = ({ values, name, label, onChange, options }) => html`
  <label>${label}<select value=${values[name]} onChange=${(event) => onChange(name, event.target.value)}>${options.map(([value, optionLabel]) => html`<option key=${value} value=${value}>${optionLabel}</option>`)}</select></label>`;
