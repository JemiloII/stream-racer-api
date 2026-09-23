import { test, expect, type Page } from "./fixtures/test";
import { installFakeEvents, emitEvent, mockJson } from "./fixtures/fake-events";
import { racingSnapshot, lobbySnapshot, endedSnapshot } from "./fixtures/sample-race";

const anythingDrawn = (page: Page) => page.evaluate(() => {
  const canvas = document.getElementById("map") as HTMLCanvasElement | null;
  const context = canvas?.getContext("2d");
  if (!canvas || !context || !canvas.width) return false;
  if (Number(getComputedStyle(canvas).opacity) === 0) return false;
  const { data } = context.getImageData(0, 0, canvas.width, canvas.height);
  for (let index = 3; index < data.length; index += 4) if (data[index]) return true;
  return false;
});

// OBS browser source (ui/minimap.html): the map box is the largest rectangle of the requested aspect that fits
// the window, centred. We read the drawn box straight off the canvas pixels (the translucent background fill).
async function drawnBoxRatio(page: Page): Promise<number | null> {
  return page.evaluate(() => {
    const canvas = document.getElementById("map") as HTMLCanvasElement | null;
    const context = canvas?.getContext("2d");
    if (!canvas || !context || !canvas.width || !canvas.height) return null;
    const { data, width, height } = context.getImageData(0, 0, canvas.width, canvas.height);
    let minX = width, minY = height, maxX = -1, maxY = -1;
    for (let y = 0; y < height; y += 1) {
      for (let x = 0; x < width; x += 1) {
        if (data[(y * width + x) * 4 + 3] === 0) continue;
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
    if (maxX < 0) return null;
    return (maxX - minX + 1) / (maxY - minY + 1);
  });
}

const parseAspect = (aspect: string): number => { const [width = 1, height = 1] = aspect.split(":").map(Number); return width / height; };

test.describe("Mini map browser source", () => {
  test.use({ viewport: { width: 800, height: 800 } });
  // the map only draws during a race, so give these a running one instead of whatever the live game is doing
  test.beforeEach(async ({ page }) => { await installFakeEvents(page); await mockJson(page, "/race", racingSnapshot()); });

  test("loads with a canvas that fills the window", async ({ page }) => {
    await page.goto("/minimap");
    const canvas = page.locator("canvas#map");
    await expect(canvas).toBeVisible();
    const box = await canvas.boundingBox();
    expect(box?.width).toBe(800);
    expect(box?.height).toBe(800);
  });

  for (const aspect of ["16:9", "9:16", "4:3"]) {
    test(`?aspect=${aspect} draws a box of that ratio`, async ({ page }) => {
      await page.goto(`/minimap?aspect=${encodeURIComponent(aspect)}&alpha=0.6`); // the box is transparent by default; give it a colour so its ratio can be read off the pixels
      await expect.poll(() => drawnBoxRatio(page), { message: `box ratio for ${aspect}` }).toBeCloseTo(parseAspect(aspect), 1);
    });
  }

  test("without ?aspect the box follows settings.minimap.aspect", async ({ page, server }) => {
    const configured = server.settings.minimap.aspect;
    test.skip(!/^\d+:\d+$/.test(configured), `aspect "${configured}" depends on the loaded track`);
    await page.goto("/minimap?alpha=0.6"); // transparent by default: give the box a colour so the pixels show its ratio
    await expect.poll(() => drawnBoxRatio(page)).toBeCloseTo(parseAspect(configured), 1);
  });
});

test.describe("Mini map race phase", () => {
  test.beforeEach(async ({ page }) => {
    await installFakeEvents(page);
    await mockJson(page, "/settings", { minimap: { alpha: 0.6, aspect: "1:1" } });
  });

  test("draws while a race is running", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/minimap");
    await expect.poll(() => anythingDrawn(page)).toBe(true);
  });

  test("clears itself when the race ends and stays gone", async ({ page }) => {
    await mockJson(page, "/race", racingSnapshot());
    await page.goto("/minimap");
    await expect.poll(() => anythingDrawn(page)).toBe(true);
    await emitEvent(page, "race_end", endedSnapshot());
    await expect.poll(() => anythingDrawn(page), { timeout: 5000 }).toBe(false);
  });

  test("stays blank in a lobby unless showInLobby is on, then comes back for the next race", async ({ page }) => {
    await mockJson(page, "/race", lobbySnapshot());
    await page.goto("/minimap");
    await expect.poll(() => anythingDrawn(page)).toBe(false);
    await emitEvent(page, "race_start", racingSnapshot());
    await expect.poll(() => anythingDrawn(page)).toBe(true);
  });

  test("?showInLobby=1 draws the track before the race starts", async ({ page }) => {
    await mockJson(page, "/race", lobbySnapshot());
    await page.goto("/minimap?showInLobby=1");
    await expect.poll(() => anythingDrawn(page)).toBe(true);
  });
});

test.describe("Map title above the mini map", () => {
  test.beforeEach(async ({ page }) => { await installFakeEvents(page); });

  test("shows the map name and its author while a race is on", async ({ page }) => {
    const race = { ...racingSnapshot(), map: { id: 234, name: "Locate Yourself", creator: "MindZoneRL" } };
    await mockJson(page, "/settings", { minimap: { alpha: 0.6 } });
    await mockJson(page, "/race", race);
    await page.goto("/minimap");
    await expect.poll(() => page.evaluate(() => {
      const canvas = document.getElementById("map") as HTMLCanvasElement;
      const context = canvas.getContext("2d")!;
      const { data, width } = context.getImageData(0, 0, canvas.width, Math.round(canvas.height * 0.08));
      for (let index = 3; index < data.length; index += 4) if (data[index]) return true;
      return width > 0 ? false : false;
    })).toBe(true);
  });

  test("?mapTitle=0 leaves that strip empty", async ({ page }) => {
    const race = { ...racingSnapshot(), map: { id: 234, name: "Locate Yourself", creator: "MindZoneRL" } };
    await mockJson(page, "/settings", { minimap: { alpha: 0 } });
    await mockJson(page, "/race", race);
    await page.goto("/minimap?mapTitle=0");
    await expect.poll(() => page.evaluate(() => {
      const canvas = document.getElementById("map") as HTMLCanvasElement;
      const context = canvas.getContext("2d")!;
      const { data } = context.getImageData(0, 0, canvas.width, Math.round(canvas.height * 0.05));
      for (let index = 3; index < data.length; index += 4) if (data[index]) return true;
      return false;
    })).toBe(false);
  });
});
