import { test, expect, card, autoJoinPeople } from "../fixtures/test";

// Shared people pieces (ui/components/roster.js): TwitchLookup, Card, Grid, Empty. Used on Bots and Settings.
test.describe("Roster components", () => {
  test("Twitch lookup box enables its button only once something is typed (no lookup is sent)", async ({ page }) => {
    await page.goto("/bots");
    const lookup = card(page, /^Bots/).locator(".lookup");
    const input = lookup.getByPlaceholder("Twitch logins or twitch.tv links, comma separated");
    const button = lookup.getByRole("button", { name: "Add Twitch" });
    await expect(button).toBeDisabled();
    await input.fill("shibikox");
    await expect(button).toBeEnabled();
    await input.fill("");
    await expect(button).toBeDisabled();
  });

  test("every bot card has tools, a picture or initials, a name and a login line", async ({ page, server }) => {
    await page.goto("/bots");
    const cards = card(page, /^Roster/).locator(".cards .card");
    await expect(cards).toHaveCount(server.settings.bots.length + server.settings.customBots.length);
    for (const botCard of await cards.all()) {
      await expect(botCard.locator(".tools button")).toHaveCount(3);
      await expect(botCard.locator(".tools button.x")).toHaveAttribute("title", "remove");
      await expect(botCard.locator("img, .ph")).toHaveCount(1);
      await expect(botCard.locator(".nm")).not.toBeEmpty();
      await expect(botCard.locator(".lg")).toHaveText(/^(custom · |@)[a-z0-9_]+( · in lobby)?$|^not on Twitch/);
    }
  });

  test("Twitch bots resolve to a profile picture or a not-on-Twitch card", async ({ page, server }) => {
    await page.goto("/bots");
    const twitchCards = card(page, /^Roster/).locator(".cards .card").filter({ hasNot: page.locator(".lg", { hasText: "custom ·" }) });
    await expect(twitchCards).toHaveCount(server.settings.bots.length);
    // Resolution is async (/twitch/users); once done each card is either a real user with an avatar or marked missing.
    for (const twitchCard of await twitchCards.all()) {
      await expect(twitchCard.locator("img").or(twitchCard.filter({ hasText: "not on Twitch" }).locator(".lg"))).toHaveCount(1);
    }
    const resolvedOrMissing = twitchCards.filter({ has: page.locator("img") }).or(twitchCards.filter({ hasText: "not on Twitch" }));
    await expect(resolvedOrMissing).toHaveCount(server.settings.bots.length);
  });

  test("the Settings auto-join list reuses the same card with an Auto-join badge and a remove tool", async ({ page, server }) => {
    const people = autoJoinPeople(server.settings);
    test.skip(!people.length, "no viewers on the auto-join list");
    await page.goto("/settings");
    const cards = card(page, /^Auto-join list/).locator(".cards .card");
    await expect(cards).toHaveCount(people.length);
    for (const personCard of await cards.all()) {
      await expect(personCard.locator(".badge")).toHaveText(["Auto-join"]);
      await expect(personCard.locator(".tools button.x")).toHaveCount(1);
    }
  });

  test("an empty list renders the Empty placeholder", async ({ page, server }) => {
    test.skip(autoJoinPeople(server.settings).length > 0, "auto-join list is not empty");
    await page.goto("/settings");
    await expect(card(page, /^Auto-join list/).locator(".empty")).toHaveText("No viewers on the list");
  });
});
