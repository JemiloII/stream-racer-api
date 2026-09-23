import { test, expect, type Page } from "./fixtures/test";
import { installFakeEvents, emitEvent, mockJson } from "./fixtures/fake-events";
import { racingSnapshot, lobbySnapshot, endedSnapshot, idleSnapshot, SAMPLE_LOGINS } from "./fixtures/sample-race";

// OBS browser source (ui/overlay.html): the horizontal bar, a track line with avatars stacked by place plus the
// RIP / finish banner. Standalone, no React. The vertical list is its own source (leaderboard.spec.ts).
// The race comes from a mocked /race and a fake event stream so the spec, not the live game, decides the phase.
const trackOpacity = (page: Page) => page.evaluate(() => Number(getComputedStyle(document.getElementById("track") as HTMLElement).opacity));

test.describe("Overlay browser source (horizontal bar)", () => {
  test.beforeEach(async ({ page }) => {
    await installFakeEvents(page);
    await mockJson(page, "/settings", { overlay: {} });
  });

  test("draws the track line, the finish flag and one avatar per car, in race order, while a race is running", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay");
    await expect(page.locator("body")).not.toHaveClass(/no-race/);
    await expect(page.locator("#track .line")).toBeVisible();
    await expect(page.locator("#track .flag")).toBeVisible();
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    await expect(page.locator("#racers .racer .place")).toHaveText(["1", "2", "3"]);
    await expect(page.locator("#racers .racer .name")).toHaveText(["Alpha", "Bravo", "Charlie"]);
    await expect(page.locator("#banner")).toBeAttached();
  });

  test("has no leaderboard of its own and ignores the old ?board= param (that list is the /leaderboard source)", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay?board=5");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    await expect(page.locator("#board")).toHaveCount(0);
    await expect(page.locator(".row")).toHaveCount(0);
  });

  test("?names=0 hides the names under the avatars", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay?names=0");
    await expect(page.locator("body")).toHaveClass(/no-names/);
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    await expect(page.locator("#racers .racer .name").first()).toBeHidden();
  });

  test("query params override the accent color from settings", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay?accent=%23123456");
    await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue("--hazard"))).toBe("#123456");
  });

  test("after race_end the cars fade out for about a second, then nothing is drawn until the next race starts", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);

    await emitEvent(page, "race_end", endedSnapshot());
    await expect(page.locator("body")).toHaveClass(/race-over/);
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length); // still drawn while fading
    await expect.poll(() => trackOpacity(page), { message: "the track fades rather than vanishing" }).toBeLessThan(1);

    await expect(page.locator("body")).toHaveClass(/no-race/, { timeout: 3000 });
    await expect(page.locator("#racers .racer")).toHaveCount(0);
    await expect(page.locator("#track")).toBeHidden();

    // The next lobby does not bring the bar back (showInLobby is off by default); the next race start does.
    await emitEvent(page, "lobby", lobbySnapshot());
    await expect(page.locator("#track")).toBeHidden();
    await expect(page.locator("#racers .racer")).toHaveCount(0);
    await emitEvent(page, "race_start", racingSnapshot());
    await expect(page.locator("#track")).toBeVisible();
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    await expect.poll(() => trackOpacity(page)).toBe(1);
  });

  test("the finish banner stays up for a few seconds after the cars have gone", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/overlay");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    await emitEvent(page, "finisher", { login: "alpha_racer", displayName: "Alpha", place: 1 });
    const banner = page.locator("#banner");
    await expect(banner).toHaveClass(/show/);
    await expect(banner).toHaveClass(/fin/);
    await expect(banner).toHaveText("Alpha finished 1st");
    await emitEvent(page, "race_end", endedSnapshot());
    await expect(page.locator("#racers .racer")).toHaveCount(0, { timeout: 3000 });
    await expect(banner).toHaveClass(/show/); // the banner runs on its own 3 s timer, longer than the 1 s fade
    await expect(banner).toBeVisible();
  });

  test("draws nothing in a lobby unless settings.overlay.showInLobby is on", async ({ page }) => {
    await mockJson(page, "/race", lobbySnapshot());
    await page.goto("/overlay");
    await expect(page.locator("body")).toHaveClass(/no-race/);
    await expect(page.locator("#track")).toBeHidden();
    await expect(page.locator("#racers .racer")).toHaveCount(0);

    await emitEvent(page, "settings", { overlay: { showInLobby: true } });
    await expect(page.locator("#track")).toBeVisible();
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);

    await emitEvent(page, "settings", { overlay: { showInLobby: false } });
    await expect(page.locator("#track")).toBeHidden();
    await expect(page.locator("#racers .racer")).toHaveCount(0);
  });

  test("draws nothing on the post-game or home screen even though the cars are still listed", async ({ page }) => {
    await mockJson(page, "/race", idleSnapshot());
    await page.goto("/overlay");
    await expect(page.locator("body")).toHaveClass(/no-race/);
    await expect(page.locator("#track")).toBeHidden();
    await expect(page.locator("#racers .racer")).toHaveCount(0);
  });
});

test.describe("Horizontal bar spacing", () => {
  test.beforeEach(async ({ page }) => { await installFakeEvents(page); });
  // the sample field at 100% each: a live race would end the phase, so use a running snapshot with everyone on the line
  const bunched = () => ({ ...racingSnapshot(), vehicles: racingSnapshot().vehicles.map((vehicle) => ({ ...vehicle, pct: 100 })) });

  test("cars that finish together are spread out by place instead of stacking (min gap)", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: { size: 40 } });
    await mockJson(page, "/race", bunched());
    await page.goto("/overlay");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    const lefts = await page.locator("#racers .racer").evaluateAll((cars) => cars.map((car) => ({ place: +(car.querySelector(".place")?.textContent || 0), left: parseFloat((car as HTMLElement).style.left) })));
    const byPlace = [...lefts].sort((a, b) => a.place - b.place);
    for (let i = 1; i < byPlace.length; i++) expect(byPlace[i - 1]!.left - byPlace[i]!.left).toBeGreaterThanOrEqual(34); // 85% of 40
  });

  test("?spread=0 keeps the raw positions", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: { size: 40 } });
    await mockJson(page, "/race", bunched());
    await page.goto("/overlay?spread=0");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    const lefts = await page.locator("#racers .racer").evaluateAll((cars) => cars.map((car) => parseFloat((car as HTMLElement).style.left)));
    expect(new Set(lefts.map((value) => Math.round(value))).size).toBe(1);
  });
});

test.describe("Horizontal bar placement", () => {
  test.beforeEach(async ({ page }) => { await installFakeEvents(page); });

  test("the bar sits in the middle of its own window, not pinned to the bottom", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: { size: 40 } });
    await mockJson(page, "/race", racingSnapshot());
    await page.setViewportSize({ width: 1200, height: 600 });
    await page.goto("/overlay");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    const box = (await page.locator("#track").boundingBox())!;
    expect(box.y + box.height / 2).toBeCloseTo(300, 0);
  });

  test("?offsetY nudges it down", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: { size: 40 } });
    await mockJson(page, "/race", racingSnapshot());
    await page.setViewportSize({ width: 1200, height: 600 });
    await page.goto("/overlay?offsetY=50");
    await expect(page.locator("#racers .racer")).toHaveCount(SAMPLE_LOGINS.length);
    const box = (await page.locator("#track").boundingBox())!;
    expect(box.y + box.height / 2).toBeCloseTo(350, 0);
  });
});
