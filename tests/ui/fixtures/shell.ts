import { expect, type Page } from "@playwright/test";
import type { ServerVersion } from "./server";

export const TAB_LABELS = ["Controls", "Camera", "Bots", "Settings", "API"] as const;
export type AppPage = "controls" | "camera" | "bots" | "settings" | "api";
export const TAB_FOR_PAGE: Record<AppPage, (typeof TAB_LABELS)[number]> = { controls: "Controls", camera: "Camera", bots: "Bots", settings: "Settings", api: "API" };

export const versionTag = (page: Page) => page.locator("header.topbar a.tag.ver");
export const tabs = (page: Page) => page.locator("header.topbar nav.tabs button");
export const activeTab = (page: Page) => page.locator("header.topbar nav.tabs button.active");

/** What every control page shares: the five tabs in order, the right one active, and the version tag from /version. */
export async function expectShell(page: Page, current: AppPage, version: ServerVersion): Promise<void> {
  await expect(tabs(page)).toHaveText([...TAB_LABELS]);
  await expect(activeTab(page)).toHaveText(TAB_FOR_PAGE[current]);
  await expect(page).toHaveURL(new RegExp(`/${current}$`));
  await expect(versionTag(page)).toHaveText(/^v\d+\.\d+\.\d+/);
  await expect(versionTag(page)).toContainText(`v${version.api}`);
}
