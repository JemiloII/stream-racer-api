import { readFileSync } from "node:fs";
import { test, expect } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

// The documented routes are the GROUPS table in ui/pages/api.js; parse it so the test follows the docs.
interface DocumentedRoute { method: string; path: string }
function documentedRoutes(): DocumentedRoute[] {
  const source = readFileSync(new URL("../../../ui/pages/api.js", import.meta.url), "utf8");
  return [...source.matchAll(/\{ m: "(GET|POST|PUT|DELETE)", p: "([^"]+)"/g)].map(([, method = "", path = ""]) => ({ method, path }));
}

test.describe("API page", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/api"); });

  test("renders the shell", async ({ page, server }) => {
    await expectShell(page, "api", server.version);
  });

  test("shows one card per documented route with its method and path", async ({ page }) => {
    const routes = documentedRoutes();
    expect(routes.length).toBeGreaterThan(0);
    const cards = page.locator(".route");
    await expect(cards).toHaveCount(routes.length);
    await expect(page.locator(".route .route-head code.path")).toHaveText(routes.map((route) => route.path));
    await expect(page.locator(".route .route-head .m")).toHaveText(routes.map((route) => route.method));
  });

  test("every route card opens to a Try it panel with an Execute button and copyable examples", async ({ page }) => {
    const heads = page.locator(".route .route-head");
    const total = await heads.count();
    for (let index = 0; index < total; index += 1) await heads.nth(index).click();
    await expect(page.locator(".route.open")).toHaveCount(total);
    await expect(page.locator(".route.open h4", { hasText: "Try it" })).toHaveCount(total);
    await expect(page.locator(".route.open .try button", { hasText: "Execute" })).toHaveCount(total);
    // Example response and curl blocks each carry a copy button.
    await expect(page.locator(".route.open .code button.copy")).toHaveCount(total * 2);
    for (const routeCard of await page.locator(".route.open").all()) {
      await expect(routeCard.locator(".try code.url")).toHaveText(/^(GET|POST|PUT|DELETE) \//);
    }
  });

  test("the filter box narrows the route list and clearing it restores every route", async ({ page }) => {
    const total = documentedRoutes().length;
    const filter = page.getByPlaceholder("filter routes");
    await filter.fill("/camera/");
    const shown = page.locator(".route .route-head code.path");
    await expect.poll(() => shown.count()).toBeLessThan(total);
    expect(await shown.count()).toBeGreaterThan(0);
    for (const path of await shown.allTextContents()) expect(path).toContain("/camera");
    await filter.fill("no such route zzz");
    await expect(page.locator(".route")).toHaveCount(0);
    await filter.fill("");
    await expect(page.locator(".route")).toHaveCount(total);
  });

  test("Events and Client sample tabs render their reference content", async ({ page }) => {
    const tabs = page.locator(".tabs2 button");
    await expect(tabs).toHaveText(["Routes", "Events (SSE)", "Client sample"]);
    await tabs.filter({ hasText: "Events" }).click();
    await expect(page.locator("main article header", { hasText: "GET /events" })).toBeVisible();
    await expect(page.locator("table.params tr")).toContainText(["lobby", "pos", "race_end"]);
    await expect(page.getByRole("button", { name: "Listen to /events" })).toBeVisible();
    await tabs.filter({ hasText: "Client sample" }).click();
    await expect(page.locator(".code pre")).toContainText("new EventSource");
    await expect(page.locator(".code button.copy")).toHaveText("copy");
  });
});
