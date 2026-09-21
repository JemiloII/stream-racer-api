import { test, expect, card } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

const SHOTS = ["Wide follow", "Grid", "Pack", "Sweep", "Side", "High", "Front leader", "Chase leader", "Orbit leader", "Track cam", "Overhead", "Boom cam", "Game follow", "Free"];

test.describe("Camera page", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/camera"); });

  test("renders the shell", async ({ page, server }) => {
    await expectShell(page, "camera", server.version);
  });

  test("Camera card has the Auto toggle and every shot button", async ({ page }) => {
    const camera = card(page, /^Camera$/);
    await expect(camera.getByRole("button", { name: /^Auto: (ON|off)$/ })).toBeVisible();
    for (const shot of SHOTS) await expect(camera.getByRole("button", { name: shot, exact: true })).toBeVisible();
    await expect(camera.locator(".cam-row button")).toHaveCount(SHOTS.length + 1);
  });

  test("Director card explains the auto director", async ({ page }) => {
    await expect(card(page, /^Director$/).locator("p.hint")).toContainText("Auto keeps most of the field in frame");
  });

  test("the mini map toggle lives on Settings, not here", async ({ page }) => {
    await expect(page.locator("main").getByRole("switch")).toHaveCount(0);
    await expect(page.locator("main").getByText(/mini ?map/i)).toHaveCount(0);
  });

  test("the racers list is shown next to the camera controls", async ({ page, server }) => {
    const board = page.locator("article.board");
    if (server.race.vehicles.length) await expect(board.locator(".row-r")).toHaveCount(server.race.vehicles.length);
    else await expect(board.locator(".board-empty")).toHaveText("No cars on track");
  });
});
