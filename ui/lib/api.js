// The control page's fetch wrapper for the plugin's JSON routes, plus the per-browser API token
// (localStorage; the only setting that is not stored server-side).
import { toast } from "./toast.js";

const TOKEN_KEY = "sr.token";
export const getToken = () => { try { return localStorage.getItem(TOKEN_KEY) || ""; } catch { return ""; } };
export const setToken = (token) => { try { localStorage.setItem(TOKEN_KEY, token || ""); } catch {} };
export const authHeaders = () => (getToken() ? { Authorization: "Bearer " + getToken() } : {});
// EventSource cannot send headers, so the stream URL carries the token as ?token=.
export const withToken = (path) => (getToken() ? path + (path.includes("?") ? "&" : "?") + "token=" + encodeURIComponent(getToken()) : path);

// JSON call to the plugin: answers the parsed body, or null on any failure. Non-GET calls toast their outcome
// (`/boom/all · 6`); a 401 or an unreachable game toasts too.
export async function api(path, { method = "POST", body } = {}) {
  try {
    const response = await fetch(path, { method, headers: { "Content-Type": "application/json", ...authHeaders() }, body: body === undefined ? (method === "GET" ? undefined : "") : JSON.stringify(body) });
    if (response.status === 401) { toast("api token required — see Settings", true); return null; }
    const payload = await response.json().catch(() => ({}));
    if (method !== "GET") toast(response.ok ? `${path.split("?")[0]} · ${payload.affected ?? "ok"}` : `${payload.error || response.status}`, !response.ok);
    return response.ok ? payload : null;
  } catch { toast("game not running", true); return null; }
}
