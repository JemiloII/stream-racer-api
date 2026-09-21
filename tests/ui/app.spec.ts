import { test, expect } from "./fixtures/test";
import { activeTab, expectShell, tabs, TAB_LABELS, versionTag, type AppPage } from "./fixtures/shell";

const PAGES: AppPage[] = ["controls", "camera", "bots", "settings", "api"];

test.describe("app shell", () => {
  for (const pageName of PAGES) {
    test(`/${pageName} shows the header tabs in order, marks its own tab active and reports the server version`, async ({ page, server }) => {
      await page.goto(`/${pageName}`);
      await expectShell(page, pageName, server.version);
    });
  }

  test("the version tag matches /version exactly, commit included", async ({ page, server }) => {
    await page.goto("/controls");
    const { api, commit } = server.version;
    const expected = commit && commit !== "dev" ? `v${api} · ${commit}` : `v${api}`;
    await expect(versionTag(page)).toHaveText(expected);
    await expect(versionTag(page)).toHaveAttribute("target", "_blank");
  });

  test("the root path opens the Controls page", async ({ page }) => {
    await page.goto("/");
    await expect(activeTab(page)).toHaveText("Controls");
    await expect(tabs(page)).toHaveText([...TAB_LABELS]);
  });

  for (const pageName of PAGES) {
    test(`an old #/${pageName} link is rewritten to /${pageName}`, async ({ page, server }) => {
      await page.goto(`/#/${pageName}`);
      await expectShell(page, pageName, server.version);
      await expect.poll(() => page.evaluate(() => location.hash)).toBe("");
    });
  }

  test("clicking a tab pushes a history entry and back/forward walk the pages", async ({ page }) => {
    await page.goto("/controls");
    await tabs(page).filter({ hasText: "Camera" }).click();
    await expect(page).toHaveURL(/\/camera$/);
    await expect(activeTab(page)).toHaveText("Camera");

    await tabs(page).filter({ hasText: "Bots" }).click();
    await expect(page).toHaveURL(/\/bots$/);
    await expect(activeTab(page)).toHaveText("Bots");

    await page.goBack();
    await expect(page).toHaveURL(/\/camera$/);
    await expect(activeTab(page)).toHaveText("Camera");

    await page.goBack();
    await expect(page).toHaveURL(/\/controls$/);
    await expect(activeTab(page)).toHaveText("Controls");

    await page.goForward();
    await expect(page).toHaveURL(/\/camera$/);
    await expect(activeTab(page)).toHaveText("Camera");
    // Tabs are client-side routing: the document never reloaded, so the same five buttons are still mounted.
    await expect(tabs(page)).toHaveText([...TAB_LABELS]);
  });
});
