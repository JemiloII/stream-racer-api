import { test, expect, card } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

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
