import { test, expect, card, type RaceSnapshot } from "../fixtures/test";
import { installFakeEvents, emitEvent } from "../fixtures/fake-events";

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

// Boost pools change between the 4 Hz snapshots; the store patches them from the `boost` / `boosts` events at once.
test.describe("Racers board · live boost counters", () => {
  test("boosts-left counters follow the boost and boosts events without waiting for the next positions snapshot", async ({ page, server }) => {
    const vehicles = server.race.vehicles;
    test.skip(!vehicles.length, "no cars on track");
    await installFakeEvents(page); // the real stream is never opened, so no live snapshot can overwrite the patched value
    await page.goto("/controls");
    const rows = page.locator("article.board .row-r");
    await expect(rows).toHaveCount(vehicles.length);

    const streamerCar = vehicles.find((vehicle) => vehicle.login === server.race.streamer);
    const target = streamerCar ?? (vehicles[0] as RaceSnapshot["vehicles"][number]);
    const row = rows.filter({ has: page.locator(".lg", { hasText: new RegExp(`^${escapeRegExp(target.login)}( · sub)?$`) }) });
    const counter = row.locator(".quick .boosts b");
    await expect(counter).toHaveText(String(target.boosts));

    await emitEvent(page, "boosts", { login: target.login, boosts: target.boosts + 40 });
    await expect(counter).toHaveText(String(target.boosts + 40));
    await emitEvent(page, "boost", { login: target.login, displayName: target.displayName, boosts: target.boosts + 39 });
    await expect(counter).toHaveText(String(target.boosts + 39));
    if (streamerCar) await expect(card(page, /^Driver$/).locator(".hero-meta b").first()).toHaveText(String(target.boosts + 39));

    // The next full snapshot is the truth again.
    await emitEvent(page, "positions", server.race);
    await expect(counter).toHaveText(String(target.boosts));
    if (streamerCar) await expect(card(page, /^Driver$/).locator(".hero-meta b").first()).toHaveText(String(target.boosts));
  });
});

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
    // during a live race the order can shift between the API fetch and this check: compare as sets
    const shownLogins = (await rows.locator(".lg").allTextContents()).map((text) => text.split(" ")[0]).sort();
    expect(shownLogins).toEqual(vehicles.map((vehicle) => vehicle.login).sort());
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
