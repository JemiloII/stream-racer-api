import { test, expect, type Page } from "./fixtures/test";
import { installFakeEvents, emitEvent, mockJson } from "./fixtures/fake-events";
import { racingSnapshot, lobbySnapshot, endedSnapshot, SAMPLE_LOGINS } from "./fixtures/sample-race";

// OBS browser source (ui/leaderboard.html): the vertical top-N list, separate from the horizontal bar (/overlay).
// Query: ?rows=10&side=left|right&scale=1&token=; without them it follows settings.overlay board / boardSide / boardScale.
const boardScale = (page: Page) => page.evaluate(() => document.documentElement.style.getPropertyValue("--board-scale"));

test.describe("Leaderboard browser source (vertical top-N list)", () => {
  test.beforeEach(async ({ page }) => {
    await installFakeEvents(page);
    await mockJson(page, "/settings", { overlay: { board: 10, boardSide: "left", boardScale: 1 } });
  });

  test("lists the cars in race order with place, name and progress while a race is running", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    await expect(page.locator("body")).not.toHaveClass(/no-race/);
    const rows = page.locator("#board .row");
    await expect(rows).toHaveCount(SAMPLE_LOGINS.length);
    await expect(rows.locator(".n")).toHaveText(["1", "2", "3"]);
    await expect(rows.locator(".nm")).toContainText(["Alpha", "Bravo", "Charlie"]);
    await expect(rows.locator(".pct")).toHaveText(["61%", "45%", "12%"]);
    await expect(page.locator("#track, #racers, #banner")).toHaveCount(0); // the bar is the other source
  });

  test("?rows= caps the list, ?side=right anchors it to the right and ?scale= sizes it", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard?rows=2&side=right&scale=0.5");
    await expect(page.locator("#board .row")).toHaveCount(2);
    await expect(page.locator("#board .row .n")).toHaveText(["1", "2"]);
    await expect(page.locator("body")).toHaveClass(/board-right/);
    await expect.poll(() => boardScale(page)).toBe("0.5");
    const board = await page.locator("#board").boundingBox();
    const viewport = page.viewportSize();
    expect(board && viewport && board.x + board.width > viewport.width / 2, "anchored on the right half").toBe(true);
  });

  test("?rows=0 turns the list off", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard?rows=0");
    await expect(page.locator("body")).not.toHaveClass(/no-race/);
    await expect(page.locator("#board")).toBeEmpty();
  });

  test("without query params it follows settings.overlay board / boardSide / boardScale and updates live on the settings event", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: { board: 1, boardSide: "right", boardScale: 0.8 } });
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    await expect(page.locator("#board .row")).toHaveCount(1);
    await expect(page.locator("body")).toHaveClass(/board-right/);
    await expect.poll(() => boardScale(page)).toBe("0.8");

    await emitEvent(page, "settings", { overlay: { board: 3, boardSide: "left", boardScale: 1.2 } });
    await expect(page.locator("#board .row")).toHaveCount(3);
    await expect(page.locator("body")).not.toHaveClass(/board-right/);
    await expect.poll(() => boardScale(page)).toBe("1.2");
  });

  test("clears after race_end and stays empty in a lobby unless settings.overlay.showInLobby is on", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    await expect(page.locator("#board .row")).toHaveCount(SAMPLE_LOGINS.length);

    await emitEvent(page, "race_end", endedSnapshot());
    await expect(page.locator("body")).toHaveClass(/race-over/);
    await expect(page.locator("#board .row .pct")).toHaveText(["FIN", "FIN", "FIN"]); // final standings while fading
    await expect(page.locator("body")).toHaveClass(/no-race/, { timeout: 3000 });
    await expect(page.locator("#board")).toBeEmpty();

    await emitEvent(page, "lobby", lobbySnapshot());
    await expect(page.locator("#board")).toBeEmpty();
    await emitEvent(page, "settings", { overlay: { showInLobby: true } });
    await expect(page.locator("#board .row")).toHaveCount(SAMPLE_LOGINS.length);
    await expect(page.locator("#board")).toBeVisible();

    await emitEvent(page, "settings", { overlay: {} });
    await expect(page.locator("#board")).toBeEmpty();
    await emitEvent(page, "race_start", racingSnapshot());
    await expect(page.locator("#board .row")).toHaveCount(SAMPLE_LOGINS.length);
  });

  test("the mod serves /leaderboard as its own page", async ({ request }) => {
    const response = await request.get("/leaderboard", { headers: { Accept: "text/html" } });
    test.skip(response.status() === 404, "the installed build predates /leaderboard: rebuild (./install.sh) and restart the game");
    expect(response.ok()).toBe(true);
    expect(await response.text()).toContain('id="board"');
  });
});

test.describe("Leaderboard name colours", () => {
  test.beforeEach(async ({ page }) => { await installFakeEvents(page); });

  test("rows show the racer's colour, and ?nameColors=0 falls back to white", async ({ page }) => {
    await mockJson(page, "/settings", { overlay: {} });
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    const firstRow = page.locator(".row .nm").first();
    await expect(firstRow).toBeVisible();
    expect(await firstRow.evaluate((element) => getComputedStyle(element).color)).not.toBe("rgb(255, 255, 255)");

    await page.goto("/leaderboard?nameColors=0");
    await expect(page.locator(".row .nm").first()).toHaveCSS("color", "rgb(255, 255, 255)");
  });
});

test.describe("Leaderboard avatars and progress", () => {
  test.beforeEach(async ({ page }) => {
    await installFakeEvents(page);
    await mockJson(page, "/settings", { overlay: {} });
  });

  test("every row shows a picture and its own completion column", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    const rows = page.locator(".row");
    await expect(rows.first()).toBeVisible();
    const count = await rows.count();
    await expect(page.locator(".row .pic")).toHaveCount(count);
    await expect(page.locator(".row .pct")).toHaveCount(count);
    await expect(rows.first().locator(".pct")).toHaveText(/^\d+%$/);
  });

  test("a finished racer reads FIN instead of a percentage", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/leaderboard");
    await expect(page.locator(".row").first()).toBeVisible();
    const finished = racingSnapshot();
    finished.vehicles[0]!.finished = true;
    finished.vehicles[0]!.pct = 100;
    await emitEvent(page, "positions", finished);
    await expect(page.locator(".row .pct").first()).toHaveText("FIN");
  });

  test("a custom bot picture is served from /image/<login>", async ({ page }) => {
    const withBot = racingSnapshot();
    withBot.vehicles[0] = { ...withBot.vehicles[0]!, login: "ttschan", displayName: "TTS-chan", avatar: null, image: "C:/pictures/tts-chan.png" };
    await mockJson(page, "/race", withBot);
    await page.goto("/leaderboard");
    await expect(page.locator(".row .pic").first()).toHaveAttribute("src", "/image/ttschan");
  });
});
