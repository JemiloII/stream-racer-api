// Settings page: one card per concern, each in ui/pages/settings/<Name>Card.js. Everything here is saved
// server-side through PUT /settings (shared by every browser) except the per-browser API token.
import { html, useCss } from "../lib/html.js";
import StreamerCard from "./settings/StreamerCard.js";
import AutoJoinCard from "./settings/AutoJoinCard.js";
import PerksCard from "./settings/PerksCard.js";
import PagesCard from "./settings/PagesCard.js";
import ChatCommandsCard from "./settings/ChatCommandsCard.js";
import NamesAndColorsCard from "./settings/NamesAndColorsCard.js";
import WebhooksCard from "./settings/WebhooksCard.js";
import MinimapCard from "./settings/MinimapCard.js";
import OverlayCard from "./settings/OverlayCard.js";
import ApiTokenCard from "./settings/ApiTokenCard.js";
import PluginConfigCard from "./settings/PluginConfigCard.js";

export default function SettingsPage() {
  useCss("pages/settings.css");
  return html`
    <div class="settings">
      <${StreamerCard} />
      <${AutoJoinCard} />
      <${PerksCard} />
      <${PagesCard} />
      <${ChatCommandsCard} />
      <${NamesAndColorsCard} />
      <${WebhooksCard} />
      <${MinimapCard} />
      <${OverlayCard} />
      <${ApiTokenCard} />
      <${PluginConfigCard} />
    </div>`;
}
