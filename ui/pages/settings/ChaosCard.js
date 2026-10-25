// Settings → Chaos events: the random events that happen during a race (meteors, dragon, tornado, air raid, witch,
// magical girl, black hole), how often, which ones, and the Gigantify-an-emote reaction. Saved as settings.chaos.
import { html } from "../../lib/html.js";
import { useStore } from "../../store.js";
import { NumberField } from "./fields.js";

export const CHAOS_EVENTS = [
  ["meteors", "Meteor shower", "booms who it hits, leaves holes in the road"],
  ["dragon", "Dragon", "fireballs set cars on fire: boom in 0-10 s, fire spreads on touch"],
  ["tornado", "Tornado", "sucks cars in"],
  ["raid", "Air raid", "a bomber squadron drops bombs on the road"],
  ["witch", "Witch", "turns cars into surfing skeletons, bats or rolling pumpkins"],
  ["magical", "Magical girl", "turns cars into cinnamon rolls, rice balls or stars"],
  ["blackhole", "Black hole", "night falls; cars that leave the ground get sucked up and respawn"],
  ["uma", "Uma race", "a whole race where every car is a horse girl and an Umamusume race track plays"],
  ["umagym", "Uma race (gym clothes)", "the same, everyone in the gym uniform"],
];

const CHAOS_DEFAULTS = {
  enabled: true,
  inRanked: true,
  minSeconds: 25,
  maxSeconds: 60,
  events: {},
  giantify: true,
  giantifyBits: 100,
  umaRaceChance: 0.2,
  weatherChance: 0.35,
};

export default function ChaosCard() {
  const { settings, saveSettings } = useStore();
  const chaos = { ...CHAOS_DEFAULTS, ...settings.chaos };
  const save = (name, value) => saveSettings({ chaos: { ...chaos, [name]: value } });
  const setEvent = (name, on) => save("events", { ...chaos.events, [name]: on });
  const ranked = settings.sendResults !== false;
  return html`
    <article>
      <header>Chaos events</header>
      ${ranked && !chaos.inRanked ? html`<p class="hint">Ranked is on and chaos is off for ranked races, so no chaos happens.</p>` : null}
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${chaos.enabled} onChange=${(event) => save("enabled", event.target.checked)} />
        <span>Random events happen on their own during a race</span>
      </label>
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${chaos.inRanked} onChange=${(event) => save("inRanked", event.target.checked)} />
        <span>Chaos in ranked races too <small class="hint">respawns and extra boosts stay off in ranked either way</small></span>
      </label>
      <div class="ovgrid">
        <${NumberField} values=${chaos} name="minSeconds" label="Every (shortest, seconds)" step="1" onChange=${save} />
        <${NumberField} values=${chaos} name="maxSeconds" label="Every (longest, seconds)" step="1" onChange=${save} />
      </div>
      ${CHAOS_EVENTS.map(([name, label, hint]) => html`
        <label class="switch-row" key=${name}>
          <input type="checkbox" role="switch" checked=${chaos.events[name] !== false} onChange=${(event) => setEvent(name, event.target.checked)} />
          <span>${label} <small class="hint">${hint}</small></span>
        </label>`)}
      <${NumberField} values=${chaos} name="weatherChance" label="Chance a race has rain, snow or fog the whole way (0 to 1)" step="0.05" onChange=${save} />
      <${NumberField} values=${chaos} name="umaRaceChance" label="Chance a race is an Uma race (0 to 1)" step="0.05" onChange=${save} />
      <label class="switch-row">
        <input type="checkbox" role="switch" checked=${chaos.giantify} onChange=${(event) => save("giantify", event.target.checked)} />
        <span>Gigantify an Emote: one boom per 10 bits, and the emote gets painted on their car and the road</span>
      </label>
      <${NumberField} values=${chaos} name="giantifyBits" label="What Gigantify costs on your channel (bits)" step="1" onChange=${save} />
    </article>`;
}
