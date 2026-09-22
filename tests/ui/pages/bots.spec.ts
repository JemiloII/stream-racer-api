import { test, expect, card, type Page } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

interface LayoutReport {
  pageOverflowsSideways: boolean;
  /** "login: selector" for every card element drawn outside its card. */
  outsideCard: string[];
  /** "login: a × b" for tools / badges / picture boxes that overlap. */
  collisions: string[];
  /** Text lines that could spill past the card edge without being clipped. */
  unclippedText: string[];
  /** Custom-bot form: per visual row, the bottom edge of every control in it (they must all match). */
  formRows: Array<{ top: number; bottoms: number[] }>;
}

// Geometry straight from the DOM: the same checks a person makes when the page "looks broken".
async function measureLayout(page: Page): Promise<LayoutReport> {
  return page.evaluate(() => {
    const round = (value: number) => Math.round(value);
    const overlaps = (a: DOMRect, b: DOMRect) => a.left < b.right - 1 && b.left < a.right - 1 && a.top < b.bottom - 1 && b.top < a.bottom - 1;
    const label = (element: Element) => element.tagName.toLowerCase() + (element.className && typeof element.className === "string" ? "." + element.className.trim().split(/\s+/).join(".") : "");
    const report = { pageOverflowsSideways: document.documentElement.scrollWidth > window.innerWidth, outsideCard: [] as string[], collisions: [] as string[], unclippedText: [] as string[], formRows: [] as Array<{ top: number; bottoms: number[] }> };

    for (const cardElement of document.querySelectorAll<HTMLElement>(".cards .card")) {
      const login = cardElement.querySelector(".lg")?.textContent?.trim() || "?";
      const box = cardElement.getBoundingClientRect();
      for (const element of cardElement.querySelectorAll<HTMLElement>("*")) {
        const rect = element.getBoundingClientRect();
        if (!rect.width || !rect.height) continue; // hidden inputs, empty spans
        if (rect.left < box.left - 0.5 || rect.right > box.right + 0.5 || rect.top < box.top - 0.5 || rect.bottom > box.bottom + 0.5) report.outsideCard.push(`${login}: ${label(element)}`);
      }
      const tools = cardElement.querySelector(".tools")?.getBoundingClientRect();
      const badges = cardElement.querySelector(".badges");
      const badgesBox = badges && badges.children.length ? badges.getBoundingClientRect() : null;
      const picture = cardElement.querySelector("img, .ph")?.getBoundingClientRect();
      if (tools && badgesBox && overlaps(tools, badgesBox)) report.collisions.push(`${login}: tools × badges`);
      if (picture && badgesBox && overlaps(picture, badgesBox)) report.collisions.push(`${login}: badges × picture`);
      if (tools && picture && overlaps(tools, picture)) report.collisions.push(`${login}: tools × picture`);
      for (const text of cardElement.querySelectorAll<HTMLElement>(".nm, .lg")) {
        if (text.scrollWidth > text.clientWidth + 1 && getComputedStyle(text).overflowX !== "hidden") report.unclippedText.push(`${login}: ${label(text)}`);
      }
    }

    const rows = new Map<number, number[]>();
    for (const item of document.querySelectorAll<HTMLElement>(".cbform > *")) {
      const control = item.tagName === "LABEL" ? item.lastElementChild : item; // the label's text is a text node; its control is the element
      if (!control) continue;
      const rowTop = round(item.getBoundingClientRect().top);
      const bottoms = rows.get(rowTop) ?? [];
      bottoms.push(round(control.getBoundingClientRect().bottom));
      rows.set(rowTop, bottoms);
    }
    report.formRows = [...rows.entries()].map(([top, bottoms]) => ({ top, bottoms }));
    return report;
  });
}

for (const viewport of [{ width: 1400, height: 1100 }, { width: 1000, height: 800 }]) {
  test.describe(`Bots page layout at ${viewport.width}×${viewport.height}`, () => {
    test.use({ viewport });

    test("nothing spills out of its card, badges keep clear of the tools and the picture, and the custom-bot form controls share one baseline per row", async ({ page, server }) => {
      await page.goto("/bots");
      const { bots = [], customBots = [] } = server.settings;
      const roster = card(page, /^Roster/);
      await expect(roster.locator(".cards .card")).toHaveCount(bots.length + customBots.length);
      // Twitch cards swap their initials for the real avatar (or turn into a not-on-Twitch card) once /twitch/users answers; measure the settled page.
      const twitchCards = roster.locator(".cards .card").filter({ hasNot: page.locator(".lg", { hasText: "custom ·" }) });
      await expect(twitchCards.filter({ has: page.locator("img") }).or(twitchCards.filter({ hasText: "not on Twitch" }))).toHaveCount(bots.length, { timeout: 15_000 });
      await page.evaluate(() => document.fonts.ready);

      const layout = await measureLayout(page);
      expect(layout.pageOverflowsSideways, "the page must not scroll sideways").toBe(false);
      expect(layout.outsideCard, "elements drawn outside their card").toEqual([]);
      expect(layout.collisions, "overlapping card pieces").toEqual([]);
      expect(layout.unclippedText, "text that can run past the card edge").toEqual([]);
      expect(layout.formRows.length, "the form has at least one row").toBeGreaterThan(0);
      for (const row of layout.formRows) {
        const distinctBottoms = [...new Set(row.bottoms)];
        expect(distinctBottoms, `controls in the form row at y=${row.top} end on the same baseline`).toHaveLength(1);
      }
      const addCustom = card(page, /^Bots/).getByRole("button", { name: "Add custom" });
      const buttonBox = await addCustom.boundingBox();
      expect(buttonBox && buttonBox.width < 260, "the Add custom button is sized to its label, not stretched").toBe(true);
    });
  });
}

test.describe("Bots page", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/bots"); });

  test("renders the shell", async ({ page, server }) => {
    await expectShell(page, "bots", server.version);
  });

  test("shows one card per Twitch bot and custom bot from /settings", async ({ page, server }) => {
    const { bots = [], customBots = [] } = server.settings;
    const roster = card(page, /^Roster/);
    await expect(roster.locator(".cards .card")).toHaveCount(bots.length + customBots.length);
    await expect(roster.locator("header")).toContainText(`· ${bots.length + customBots.length}`);
  });

  test("custom bots come before Twitch bots", async ({ page, server }) => {
    const { customBots = [] } = server.settings;
    test.skip(!customBots.length, "no custom bots configured");
    const cards = card(page, /^Roster/).locator(".cards .card");
    for (let index = 0; index < customBots.length; index += 1) {
      await expect(cards.nth(index).locator(".lg")).toContainText("custom ·");
    }
    await expect(cards.nth(customBots.length).locator(".lg")).not.toContainText("custom ·");
  });

  test("a starred bot wears the Auto-join badge and the roster header counts them", async ({ page, server }) => {
    const autoJoinLogins = new Set(server.settings.autoJoin.map((entry) => entry.login));
    const botLogins = [...server.settings.customBots.map((bot) => bot.login), ...server.settings.bots];
    const starredCount = botLogins.filter((login) => autoJoinLogins.has(login)).length;
    const roster = card(page, /^Roster/);
    await expect(roster.locator("header .hint-inline")).toHaveText(`${starredCount} auto-join`);

    const starredCards = roster.locator(".card").filter({ has: page.locator("button.star.on") });
    await expect(starredCards).toHaveCount(starredCount);
    for (const starred of await starredCards.all()) await expect(starred.locator(".badge.auto-join")).toHaveText("Auto-join");
    const unstarredCards = roster.locator(".card").filter({ hasNot: page.locator("button.star.on") });
    await expect(unstarredCards.locator(".badge.auto-join")).toHaveCount(0);
  });

  test("a bot with auto-boost off is badged API boosts", async ({ page, server }) => {
    const apiBoostLogins = Object.entries(server.settings.botOptions || {}).filter(([, options]) => options.autoBoost === false).map(([login]) => login);
    const roster = card(page, /^Roster/);
    await expect(roster.locator(".badge.api-boosts")).toHaveCount(apiBoostLogins.length);
    await expect(roster.locator("button.bolt:not(.on)")).toHaveCount(apiBoostLogins.length);
  });

  test("roster toolbar has select-all, none, add-selected and defaults", async ({ page }) => {
    const toolbar = card(page, /^Roster/).locator(".toolbar");
    await expect(toolbar.getByRole("button", { name: "All", exact: true })).toBeEnabled();
    await expect(toolbar.getByRole("button", { name: "None", exact: true })).toBeDisabled();
    await expect(toolbar.getByRole("button", { name: "Add selected to lobby" })).toBeDisabled();
    await expect(toolbar.getByRole("button", { name: "Defaults" })).toBeEnabled();
  });

  test("Bots card has the Twitch lookup and the custom bot form", async ({ page }) => {
    const bots = card(page, /^Bots/);
    await expect(bots.getByRole("button", { name: "Add Twitch" })).toBeDisabled();
    await expect(bots.getByRole("button", { name: "Add custom" })).toBeDisabled();
    await expect(bots.getByPlaceholder("my_bot")).toBeVisible();
  });
});
