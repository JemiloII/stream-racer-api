import { test, expect, card } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

const SHOTS = ["Wide follow", "Grid", "Pack", "Sweep", "Side", "High", "Front leader", "Chase leader", "Orbit leader", "Track cam", "Overhead", "Boom cam", "Game follow", "Free"];
// settings.camera.shots keys and the labels the page gives them (ui/pages/camera.js DIRECTOR_SHOTS).
const DIRECTOR_SHOTS: Array<[key: string, label: string]> = [
  ["grid", "Grid start"], ["high", "High overview"], ["side", "Side"], ["sweep", "Sweep"], ["pack", "Pack"], ["front", "Front leader"], ["chase", "Chase leader"],
  ["orbit", "Orbit"], ["overhead", "Overhead"], ["prop", "Track cams"], ["finish", "Finish cam"], ["duel", "Duel"], ["pileup", "Pile-up"], ["boom", "Boom orbit"],
];

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

  test("Director card has a Director uses switch per shot, on unless settings.camera.shots turns it off", async ({ page, server }) => {
    const director = card(page, /^Director$/);
    await expect(director.locator(".shots-head")).toHaveText("Director uses");
    await expect(director.getByRole("switch")).toHaveCount(DIRECTOR_SHOTS.length);
    await expect(director.locator(".shots .switch-row span")).toHaveText(DIRECTOR_SHOTS.map(([, label]) => label));
    const shots = server.settings.camera?.shots || {};
    for (const [key, label] of DIRECTOR_SHOTS) {
      await expect(director.getByRole("switch", { name: label, exact: true }), `${label} follows settings.camera.shots.${key}`).toBeChecked({ checked: shots[key] !== false });
    }
  });

  test("the mini map toggle lives on Settings, not here", async ({ page }) => {
    await expect(page.locator("main").getByRole("switch", { name: /mini ?map/i })).toHaveCount(0);
    await expect(page.locator("main").getByText(/mini ?map/i)).toHaveCount(0);
  });

  test("the racers list is shown next to the camera controls", async ({ page, server }) => {
    const board = page.locator("article.board");
    if (server.race.vehicles.length) await expect(board.locator(".row-r")).toHaveCount(server.race.vehicles.length);
    else await expect(board.locator(".board-empty")).toHaveText("No cars on track");
  });
});
