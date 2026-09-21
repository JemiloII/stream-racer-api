import { test, expect } from "../fixtures/test";

// The timing board (ui/components/racers.js) is shared by Controls and Camera; it is driven by /race.
test.describe("Racers board", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/controls"); });

  test("has a Timing header and a search box", async ({ page, server }) => {
    const board = page.locator("article.board");
    await expect(board.locator(".board-head")).toContainText("Timing");
    await expect(board.getByPlaceholder("search racers")).toBeVisible();
    if (server.race.vehicles.length) await expect(board.locator(".board-head b")).toHaveText(`· ${server.race.vehicles.length}`);
  });

  test("shows No cars on track when the field is empty", async ({ page, server }) => {
    test.skip(server.race.vehicles.length > 0, "cars are on track");
    await expect(page.locator("article.board .board-empty")).toHaveText("No cars on track");
    await expect(page.locator("article.board .row-r")).toHaveCount(0);
  });

  test("one row per vehicle, in race order, with the quick action buttons", async ({ page, server }) => {
    const vehicles = server.race.vehicles;
    test.skip(!vehicles.length, "no cars on track");
    const rows = page.locator("article.board .row-r");
    await expect(rows).toHaveCount(vehicles.length);
    await expect(rows.locator(".pos")).toHaveText(vehicles.map((vehicle) => String(vehicle.place)));
    await expect(rows.locator(".lg")).toContainText(vehicles.map((vehicle) => vehicle.login));
    for (const row of await rows.all()) {
      await expect(row.locator(".quick button")).toHaveCount(8);
      await expect(row.locator(".quick .boosts b")).toHaveText(/^\d+$/);
    }
    const streamerRow = rows.filter({ has: page.locator(".you") });
    await expect(streamerRow).toHaveCount(vehicles.some((vehicle) => vehicle.login === server.race.streamer) ? 1 : 0);
  });

  test("searching filters rows and a miss says No match", async ({ page, server }) => {
    const vehicles = server.race.vehicles;
    test.skip(!vehicles.length, "no cars on track");
    const firstLogin = vehicles[0]?.login ?? "";
    const board = page.locator("article.board");
    const search = board.getByPlaceholder("search racers");
    await search.fill("zzz-no-such-racer");
    await expect(board.locator(".board-empty")).toHaveText("No match");
    await search.fill(firstLogin);
    await expect(board.locator(".row-r").first().locator(".lg")).toContainText(firstLogin);
    await search.fill("");
    await expect(board.locator(".row-r")).toHaveCount(vehicles.length);
  });
});
