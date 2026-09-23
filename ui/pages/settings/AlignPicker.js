// Three little icon buttons: align this line left, centre or right. Used for the map title lines on the mini map.
import { html, useCss } from "../../lib/html.js";

const BARS = { left: [16, 10, 13], center: [16, 10, 13], right: [16, 10, 13] };
const offsets = { left: () => 0, center: (width, full) => (full - width) / 2, right: (width, full) => full - width };

const icon = (side) => {
  const full = 16, place = offsets[side];
  const rows = BARS[side].map((width, index) => `<rect x="${place(width, full)}" y="${3 + index * 4}" width="${width}" height="2" rx="1"></rect>`).join("");
  return html`<svg viewBox="0 0 16 14" width="16" height="14" aria-hidden="true" dangerouslySetInnerHTML=${{ __html: rows }}></svg>`;
};

/**
 * @param {{ value?: string, onChange: (side: string) => unknown, label: string }} props
 */
export default function AlignPicker({ value = "center", onChange, label }) {
  useCss("pages/settings.css");
  return html`
    <label class="align-field">${label}
      <div class="align-picker" role="group">
        ${["left", "center", "right"].map((side) => html`
          <button key=${side} type="button" class=${value === side ? "on" : ""} title=${side} aria-label=${`${label}: ${side}`} aria-pressed=${value === side} onClick=${() => onChange(side)}>${icon(side)}</button>`)}
      </div>
    </label>`;
}
