import { test, expect, type Page } from "./fixtures/test";

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
      await page.goto(`/minimap?aspect=${encodeURIComponent(aspect)}`);
      await expect.poll(() => drawnBoxRatio(page), { message: `box ratio for ${aspect}` }).toBeCloseTo(parseAspect(aspect), 1);
    });
  }

  test("without ?aspect the box follows settings.minimap.aspect", async ({ page, server }) => {
    const configured = server.settings.minimap.aspect;
    test.skip(!/^\d+:\d+$/.test(configured), `aspect "${configured}" depends on the loaded track`);
    await page.goto("/minimap");
    await expect.poll(() => drawnBoxRatio(page)).toBeCloseTo(parseAspect(configured), 1);
  });
});
