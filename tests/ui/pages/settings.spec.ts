import { test, expect, card, autoJoinPeople } from "../fixtures/test";
import { expectShell } from "../fixtures/shell";

test.describe("Settings page", () => {
  test.beforeEach(async ({ page }) => { await page.goto("/settings"); });

  test("renders the shell", async ({ page, server }) => {
    await expectShell(page, "settings", server.version);
  });

  test("API port field shows the port the plugin is listening on", async ({ page, server }) => {
    const plugin = card(page, /^Game plugin$/);
    await expect(plugin.locator(".portrow input")).toHaveValue(String(server.settings.config.port));
    await expect(plugin.locator(".portrow button", { hasText: "Apply" })).toBeDisabled();
  });

  test("chat color command field shows the saved command", async ({ page, server }) => {
    const names = card(page, /^Names & colors$/);
    await expect(names.getByLabel("Chat command")).toHaveValue(server.settings.colorCommand);
    await expect(names.locator("p.hint code").first()).toContainText(server.settings.colorCommand);
  });

  test("respawn command field shows the saved command", async ({ page, server }) => {
    await expect(card(page, /^Chat commands$/).getByLabel("Respawn command")).toHaveValue(server.settings.respawnCommand);
  });

  test("mini map switch and ratio reflect settings.minimap", async ({ page, server }) => {
    const minimap = card(page, /^Mini map$/);
    await expect(minimap.getByRole("switch", { name: /Show the mini map inside the game/ })).toBeChecked({ checked: server.settings.minimap.enabled });
    await expect(minimap.getByLabel("Ratio (in-game and browser)")).toHaveValue(server.settings.minimap.aspect);
    await expect(minimap.getByRole("switch", { name: /^Names next to dots/ })).toBeChecked({ checked: server.settings.minimap.names });
    await expect(minimap.getByRole("switch", { name: "Bigger dot for the leader" })).toBeChecked({ checked: server.settings.minimap.leaderBig });
  });

  test("mini map link builder produces a /minimap URL with aspect and names (local state only)", async ({ page }) => {
    const builder = card(page, /^Mini map$/).locator(".mmlink");
    const link = builder.locator("input[readonly]");
    await expect(link).toHaveValue(/\/minimap\?aspect=1%3A1&names=1&leaderBig=1$/);
    await builder.getByLabel("Ratio").selectOption("16:9");
    await expect(link).toHaveValue(/aspect=16%3A9/);
    await builder.getByRole("switch", { name: "Names" }).uncheck();
    await expect(link).toHaveValue(/names=0/);
    await expect(builder.getByRole("button", { name: "Copy link" })).toBeVisible();
  });

  test("auto-join list shows only non-bot viewers", async ({ page, server }) => {
    const people = autoJoinPeople(server.settings);
    const list = card(page, /^Auto-join list/);
    await expect(list.locator("header .hint-inline")).toHaveText(`${people.length} people join every lobby`);
    if (!people.length) {
      await expect(list.locator(".empty")).toHaveText("No viewers on the list");
      return;
    }
    await expect(list.locator(".cards .card")).toHaveCount(people.length);
    await expect(list.locator(".cards .card .lg")).toHaveText(people.map((person) => `@${person.login}`));
    await expect(list.locator(".cards .card .badge.auto-join")).toHaveCount(people.length);
  });

  test("You card reflects the streamer auto-join switch", async ({ page, server }) => {
    const you = card(page, /^You$/);
    await expect(you.getByRole("switch", { name: /Put my own car in every lobby/ })).toBeChecked({ checked: server.settings.autoJoinStreamer });
    await expect(you.getByRole("button", { name: "Join me now" })).toBeVisible();
  });

  test("Webhooks card lists the configured hooks and can add one", async ({ page, server }) => {
    const webhooks = card(page, /^Webhooks$/);
    await expect(webhooks.locator(".hook")).toHaveCount((server.settings.webhooks || []).length);
    await expect(webhooks.getByRole("button", { name: "Add webhook" })).toBeVisible();
  });

  test("Perks, Overlay look and API token cards are present", async ({ page, server }) => {
    await expect(card(page, /^Perks$/).getByLabel("Chat color command")).toBeVisible();
    await expect(card(page, /^Overlay look$/).getByRole("button", { name: "Reset" })).toBeVisible();
    const token = card(page, /^API token$/);
    await expect(token.locator("p.hint").first()).toContainText(server.settings.config.tokenRequired ? "A token is required" : "No token set");
  });

  test("Overlay look is split into a Horizontal bar group and a Leaderboard group that mirror settings.overlay", async ({ page, server }) => {
    const overlay = card(page, /^Overlay look$/);
    const groups = overlay.locator(".ovgroup");
    await expect(groups.locator("h4")).toContainText(["Horizontal bar", "Leaderboard"]);
    const { overlay: saved = {} } = server.settings;

    const bar = groups.nth(0);
    await expect(bar.getByLabel("Avatar size (px)")).toBeVisible();
    await expect(bar.getByRole("switch", { name: /^Names under avatars/ })).toBeChecked({ checked: saved.names !== false });
    await expect(bar.getByRole("switch", { name: /^Show the field in the lobby too/ })).toBeChecked({ checked: !!saved.showInLobby });
    await expect(bar.getByText(/Leaderboard rows/)).toHaveCount(0); // the list has its own group now

    const board = groups.nth(1);
    await expect(board.getByLabel("Rows (0 = off)")).toHaveValue(String(saved.board ?? 10));
    await expect(board.getByLabel("Size (scale, 1 = 100%)")).toHaveValue(String(saved.boardScale ?? 1));
    await expect(board.getByLabel("Anchor side")).toHaveValue(saved.boardSide ?? "left");
    await expect(board.locator("p.hint").first()).toContainText("mini map fits above it");
  });

  test("leaderboard link builder produces a /leaderboard URL with rows, side and scale (local state only)", async ({ page, server }) => {
    const { overlay: saved = {} } = server.settings;
    const builder = card(page, /^Overlay look$/).locator(".lblink");
    const link = builder.locator("input[readonly]");
    await expect(link).toHaveValue(`${new URL(page.url()).origin}/leaderboard?rows=${saved.board ?? 10}&side=${saved.boardSide ?? "left"}&scale=${saved.boardScale ?? 1}`);
    await builder.getByLabel("Rows").fill("5");
    await expect(link).toHaveValue(/\/leaderboard\?rows=5&/);
    await builder.getByLabel("Side").selectOption("right");
    await expect(link).toHaveValue(/&side=right&/);
    await builder.getByLabel("Scale").fill("0.6");
    await expect(link).toHaveValue(/&scale=0\.6$/);
    await expect(builder.getByRole("button", { name: "Copy link" })).toBeVisible();
  });
});
