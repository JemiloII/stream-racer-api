// Entry point of the control page: the header (race status, streamer, version tag, tabs) and the page router.
// Routes are real paths (/controls, /camera, …); the plugin serves index.html for them and the store keeps `page`.
import { createRoot } from "react-dom/client";
import { useEffect } from "react";
import { html, useCss } from "./lib/html.js";
import { useStore } from "./store.js";
import ControlsPage from "./pages/controls.js";
import CameraPage from "./pages/camera.js";
import BotsPage from "./pages/bots.js";
import SettingsPage from "./pages/settings.js";
import ApiPage from "./pages/api.js";

const PAGES = {
  controls: { label: "Controls", component: ControlsPage },
  camera: { label: "Camera", component: CameraPage },
  bots: { label: "Bots", component: BotsPage },
  settings: { label: "Settings", component: SettingsPage },
  api: { label: "API", component: ApiPage },
};

function VersionTag({ version }) {
  const className = "tag ver " + (version.upToDate === true ? "ok" : version.upToDate === false ? "old" : "");
  const title = version.upToDate === false ? `update available: ${version.latest}` : version.upToDate ? "up to date" : "no update source set (api.UpdateUrl)";
  const commit = version.commit && version.commit !== "dev" ? " · " + version.commit : "";
  return html`<a class=${className} href=${version.updateUrl || "https://twitch.tv/ShibikoX"} target="_blank" title=${title}>v${version.api}${commit}</a>`;
}

function App() {
  useCss("app.css");
  const { page, setPage, snapshot, me, online, connect, loadSettings, version } = useStore();
  useEffect(() => { connect(); loadSettings(); }, []);
  const Page = (PAGES[page] || PAGES.controls).component;
  const [statusLabel, statusClass] = !online ? ["Offline", ""] : snapshot.running ? ["Live", "live"] : snapshot.vehicles.length ? ["Lobby", "lobby"] : ["Idle", ""];
  return html`
    <header class="topbar">
      <div class="brand">Stream Racer<small>Pit wall</small></div>
      <span class=${"tag " + statusClass}>${statusLabel}</span>
      <span class="tag">${me.login ? "@" + me.login : "not logged in"}</span>
      ${version.api ? html`<${VersionTag} version=${version} />` : null}
      <nav class="tabs">
        ${Object.entries(PAGES).map(([slug, { label }]) => html`<button key=${slug} class=${slug === page ? "active" : ""} onClick=${() => setPage(slug)}>${label}</button>`)}
      </nav>
    </header>
    <main><${Page} /></main>`;
}

createRoot(document.getElementById("root")).render(html`<${App} />`);
