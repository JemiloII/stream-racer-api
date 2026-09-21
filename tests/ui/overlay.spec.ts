import { test, expect } from "./fixtures/test";

// OBS browser source (ui/overlay.html): a track line with avatars, leaderboard and banners. Standalone, no React.
test.describe("Overlay browser source", () => {
  test("loads with the track line, finish flag and racer container", async ({ page, server }) => {
    await page.goto("/overlay");
    const track = page.locator("#track");
    await expect(track).toBeAttached();
    await expect(track.locator(".line")).toHaveCount(1);
    await expect(track.locator(".flag")).toHaveCount(1);
    await expect(page.locator("#board")).toBeAttached();
    await expect(page.locator("#banner")).toBeAttached();
    await expect(page.locator("#racers .racer")).toHaveCount(server.race.vehicles.length);
    await expect(page.locator("body")).not.toHaveClass(/no-names/);
  });

  test("?names=0 hides the names under the avatars", async ({ page, server }) => {
    await page.goto("/overlay?names=0");
    await expect(page.locator("body")).toHaveClass(/no-names/);
    if (server.race.vehicles.length) await expect(page.locator("#racers .racer .name").first()).toBeHidden();
  });

  test("?board=0 turns the leaderboard off", async ({ page }) => {
    await page.goto("/overlay?board=0");
    await expect(page.locator("#board")).toBeEmpty();
  });

  test("query params override the accent color from settings", async ({ page }) => {
    await page.goto("/overlay?accent=%23123456");
    await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue("--hazard"))).toBe("#123456");
  });
});
