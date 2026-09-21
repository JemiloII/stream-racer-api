import { createRoot } from "react-dom/client";
import { useEffect } from "react";
import { html, useStore, useCss } from "./store.js";
import Controls from "./pages/controls.js";
import Settings from "./pages/settings.js";
import Bots from "./pages/bots.js";
import ApiDocs from "./pages/api.js";
import CameraPage from "./pages/camera.js";

const PAGES = { controls: ["Controls", Controls], camera: ["Camera", CameraPage], bots: ["Bots", Bots], settings: ["Settings", Settings], api: ["API", ApiDocs] };

function App() {
  useCss("app.css");
  const { page, setPage, snap, me, online, connect, loadSettings, version } = useStore();
  useEffect(() => { connect(); loadSettings(); }, []);
  const Page = (PAGES[page] || PAGES.controls)[1];
  const state = !online ? ["Offline", ""] : snap.running ? ["Live", "live"] : snap.vehicles.length ? ["Lobby", "lobby"] : ["Idle", ""];
  return html`
    <header class="topbar">
      <div class="brand">Stream Racer<small>Pit wall</small></div>
      <span class=${"tag " + state[1]}>${state[0]}</span>
      <span class="tag">${me.login ? "@" + me.login : "not logged in"}</span>
      ${version.api ? html`<a class=${"tag ver " + (version.upToDate === true ? "ok" : version.upToDate === false ? "old" : "")} href=${version.updateUrl || "https://twitch.tv/ShibikoX"} target="_blank" title=${version.upToDate === false ? `update available: ${version.latest}` : version.upToDate ? "up to date" : "no update source set (api.UpdateUrl)"}>v${version.api}${version.commit && version.commit !== "dev" ? " · " + version.commit : ""}</a>` : null}
      <nav class="tabs">
        ${Object.entries(PAGES).map(([k, [label]]) => html`<button key=${k} class=${k === page ? "active" : ""} onClick=${() => setPage(k)}>${label}</button>`)}
      </nav>
    </header>
    <main><${Page} /></main>`;
}

createRoot(document.getElementById("root")).render(html`<${App} />`);
