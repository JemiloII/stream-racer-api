import { test as base, expect, type Page } from "@playwright/test";
export type { Page };
import { SKIP_ENV } from "./global-setup";
import { fetchServerTruth, type ServerTruth } from "./server";
import { serveWorkingTree } from "./working-tree";

export * from "./server";
export { expect };

interface Fixtures {
  /** /settings, /version and /race as the server reports them, fetched once per test. */
  server: ServerTruth;
  /** Console errors and uncaught exceptions collected for the test's page; the test fails if any were seen. */
  consoleErrors: string[];
  /** Serves ui/ from the working tree (pages, .js, .css) so the DLL's embedded copy is not what is tested. SR_UI_LIVE=1 disables it. */
  workingTreeUi: void;
}

export const test = base.extend<Fixtures>({
  server: async ({ request }, use) => {
    await use(await fetchServerTruth(request));
  },

  consoleErrors: [
    async ({ page }, use, testInfo) => {
      testInfo.skip(!!process.env[SKIP_ENV], process.env[SKIP_ENV]);
      const errors: string[] = [];
      page.on("console", (message) => { if (message.type() === "error") errors.push(`console.error: ${message.text()}`); });
      page.on("pageerror", (error) => errors.push(`uncaught: ${error.message}`));
      await use(errors);
      expect(errors, "the page logged errors or threw").toEqual([]);
    },
    { auto: true },
  ],

  workingTreeUi: [
    async ({ page, baseURL }, use) => {
      if (!process.env.SR_UI_LIVE && baseURL) await serveWorkingTree(page, new URL(baseURL).origin);
      await use();
    },
    { auto: true },
  ],
});

/** A `<main>` card (`<article>`) whose own `<header>` reads `title`. */
export function card(page: Page, title: RegExp | string) {
  return page.locator("main article").filter({ has: page.locator("header", { hasText: title }) });
}
