import { test, expect, card } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

test.describe("Controls page", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/controls"); });

  test("renders the shell", async ({ page, server }) => {
    await expectShell(page, "controls", server.version);
  });

  test("Driver card has the Boost me button, the boosts-left counter and the hotkey from the plugin config", async ({ page, server }) => {
    const driver = card(page, /^Driver$/);
    await expect(driver.getByRole("button", { name: "Boost me" })).toBeVisible();
    await expect(driver.getByText(/boosts left/)).toBeVisible();
    await expect(driver.locator("kbd")).toHaveText(server.settings.config.hotkeyBoost || "J");
  });

  test("Field card lists every crowd action with its fire button", async ({ page }) => {
    const field = card(page, /^Field$/);
    await expect(field.locator(".action .label")).toContainText(["Boom", "Boost all", "Add boosts", "Slow all", "Respawn all", "Boom everyone"]);
    await expect(field.getByRole("button", { name: "Fire" })).toHaveCount(5);
    await expect(field.getByRole("button", { name: "Add" })).toHaveCount(1);
    // Numeric inputs for boom count, add boosts, slow multiplier and slow seconds.
    await expect(field.locator("label.field input")).toHaveCount(4);
  });

  test("Race card offers the lobby and race flow buttons", async ({ page, server }) => {
    const race = card(page, /^Race$/);
    for (const name of ["Create lobby", "Start countdown", "Start now", "End race", "Next random map", "Add all of chat"]) {
      await expect(race.getByRole("button", { name, exact: true })).toBeVisible();
    }
    // Adding chat mid-race is refused by the page itself.
    const addChat = race.getByRole("button", { name: "Add all of chat" });
    if (server.race.running) await expect(addChat).toBeDisabled(); else await expect(addChat).toBeEnabled();
  });

  test("camera controls only appear on Controls when the setting says so", async ({ page, server }) => {
    const cameraCard = card(page, /^Camera$/);
    await expect(cameraCard).toHaveCount(server.settings.ui?.cameraOnControls ? 1 : 0);
  });

  test("the racers list mirrors /race", async ({ page, server }) => {
    const board = page.locator("article.board");
    const vehicles = server.race.vehicles;
    if (!vehicles.length) {
      await expect(board.locator(".board-empty")).toHaveText("No cars on track");
      return;
    }
    await expect(board.locator(".row-r")).toHaveCount(vehicles.length);
    await expect(board.locator(".row-r .nm")).toContainText(vehicles.map((vehicle) => vehicle.displayName));
  });
});
