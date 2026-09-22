import { existsSync, readFileSync } from "node:fs";
import { join, extname } from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";

// The mod serves ui/ from resources embedded at build time, so the running game shows whatever was last built.
// This fixture answers requests for those files from the working tree instead (pages, .js, .css), while every
// API call (/race, /settings, /events, …) still goes to the live game. So a UI change is testable without a
// rebuild + game restart, and the specs describe the code in the repo, not the DLL. SR_UI_LIVE=1 turns it off.
const UI_DIR = join(fileURLToPath(new URL(".", import.meta.url)), "../../../ui");
const APP_PAGES = new Set(["", "controls", "camera", "bots", "settings", "api"]);
const STANDALONE_PAGES: Record<string, string> = { overlay: "overlay.html", leaderboard: "leaderboard.html", minimap: "minimap.html" };
const MIME: Record<string, string> = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png", ".json": "application/json" };

export function workingTreeFile(pathname: string, isDocument: boolean): string | null {
  const relative = pathname.replace(/^\/+/, "");
  if (isDocument && APP_PAGES.has(relative)) return join(UI_DIR, "index.html");
  if (isDocument && STANDALONE_PAGES[relative]) return join(UI_DIR, STANDALONE_PAGES[relative] as string);
  if (!MIME[extname(relative)] || relative.includes("..")) return null;
  const file = join(UI_DIR, relative);
  return existsSync(file) ? file : null;
}

export async function serveWorkingTree(page: Page, origin: string): Promise<void> {
  await page.route((url) => url.origin === origin, async (route, request) => {
    if (request.method() !== "GET") return route.fallback();
    const url = new URL(request.url());
    const file = workingTreeFile(url.pathname, request.resourceType() === "document");
    if (!file) return route.fallback();
    return route.fulfill({ status: 200, contentType: MIME[extname(file)] as string, headers: { "Cache-Control": "no-cache" }, body: readFileSync(file) });
  });
}
