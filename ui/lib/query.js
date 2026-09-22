// Query string of a browser-source page (/overlay, /leaderboard, /minimap): `?token=` for the API when api.Token
// is set, plus the per-source look overrides each page reads itself.
export const query = new URLSearchParams(location.search);
export const token = query.get("token") || "";
export const tokenQuery = token ? "?token=" + encodeURIComponent(token) : "";
