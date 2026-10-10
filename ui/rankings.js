// Transparent OBS browser source: the standings kept on this PC (GET /standings), refreshed after every race.
// Query: ?token=  &period=day|week|month|all (default week)  &rows=10  &scale=1  &title=Weekly%20Top
import { query, token, tokenQuery } from "./lib/query.js";
import { escapeHtml } from "./lib/text.js";

const PERIOD_TITLES = { day: "Today", week: "This week", month: "This month", all: "All time" };
const period = PERIOD_TITLES[query.get("period")] ? query.get("period") : "week";
const rows = +(query.get("rows") || 10);
const tokenParam = token ? "&token=" + encodeURIComponent(token) : "";
document.documentElement.style.setProperty("--scale", query.get("scale") || 1);
document.getElementById("title").textContent = query.get("title") || PERIOD_TITLES[period];

const rowsElement = document.getElementById("rows");
function render(result) {
  rowsElement.innerHTML = result.standings.map((standing) => `
    <div class="row" style="--c:${escapeHtml(standing.color || "#fff")}">
      <span class="n">${standing.rank}</span>
      <span class="nm">${escapeHtml(standing.name || standing.login)}</span>
      <span class="wins">${standing.wins ? standing.wins + "W" : ""}</span>
      <span class="pts">${standing.points}</span>
    </div>`).join("");
}

const refresh = () => fetch(`/standings?period=${period}&limit=${rows}${tokenParam}`)
  .then((response) => response.json())
  .then(render)
  .catch(() => {});

// A new race lands at race_end; the day/week/month also rolls over on its own, so poll slowly as a backstop.
let eventSource, retryTimer;
function connect() {
  clearTimeout(retryTimer);
  eventSource?.close();
  eventSource = new EventSource("/events" + tokenQuery);
  eventSource.addEventListener("race_end", () => setTimeout(refresh, 1000));
  eventSource.onopen = refresh;
  eventSource.onerror = () => {
    eventSource.close();
    retryTimer = setTimeout(connect, 3000);
  };
}
refresh();
connect();
setInterval(refresh, 5 * 60 * 1000);
