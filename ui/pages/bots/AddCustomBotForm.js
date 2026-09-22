// Bots → "Add custom": a bot with your own login, name, color and picture (file upload or URL / local path), no Twitch account.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { api } from "../../lib/api.js";
import { readFileAsDataUrl } from "../../lib/files.js";

const DEFAULT_COLOR = "#35e0ff";
const normalizeLogin = (text) => text.trim().toLowerCase().replace(/[^a-z0-9_]/g, "");

export default function AddCustomBotForm({ onAdd }) {
  const [login, setLogin] = useState(""), [name, setName] = useState(""), [color, setColor] = useState(DEFAULT_COLOR), [autoColor, setAutoColor] = useState(true);
  const [imagePath, setImagePath] = useState(""), [file, setFile] = useState(null), [busy, setBusy] = useState(false);
  const add = async () => {
    const cleanLogin = normalizeLogin(login); if (!cleanLogin) return;
    setBusy(true);
    let image = imagePath.trim();
    if (file) {
      const upload = await api(`/image/${cleanLogin}`, { method: "PUT", body: { data: await readFileAsDataUrl(file) } });
      if (upload?.path) image = upload.path;
    }
    await onAdd({ id: "", login: cleanLogin, displayName: name.trim() || login.trim(), color: autoColor ? null : color, image: image || null });
    setLogin(""); setName(""); setImagePath(""); setFile(null); setBusy(false);
  };
  return html`
    <div class="cbform">
      <label>Custom bot login<input placeholder="my_bot" value=${login} onInput=${(event) => setLogin(event.target.value)} /></label>
      <label>Display name<input placeholder="My Bot" value=${name} onInput=${(event) => setName(event.target.value)} /></label>
      <label>Color
        <div class="colorrow">
          <button class=${"secondary" + (autoColor ? " on" : "")} onClick=${() => setAutoColor(!autoColor)} title="pick one automatically">auto</button>
          <input type="color" value=${color} disabled=${autoColor} onInput=${(event) => setColor(event.target.value)} />
        </div>
      </label>
      <label>Picture
        <label class="filebtn"><span>${file ? file.name : "Choose file…"}</span><input type="file" accept="image/*" hidden onChange=${(event) => setFile(event.target.files[0] || null)} /></label>
      </label>
      <label>…or image URL / path<input placeholder="https://… or C:/…/bot.png" value=${imagePath} onInput=${(event) => setImagePath(event.target.value)} /></label>
      <button disabled=${busy || !login.trim()} aria-busy=${busy} onClick=${add}>Add custom</button>
    </div>`;
}
