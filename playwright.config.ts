import { defineConfig, devices } from "@playwright/test";

// UI tests for the control page served by the running game. Needs StreamRacer.exe with the mod loaded;
// global-setup pings /version and, when the game is down, every test skips with a printed reason.
export const baseURL = (process.env.SR_API || "http://127.0.0.1:8793").replace(/\/$/, "");

export default defineConfig({
  testDir: "tests/ui",
  globalSetup: "./tests/ui/fixtures/global-setup.ts",
  timeout: 30_000,
  expect: { timeout: 10_000 },
  retries: 0,
  reporter: "list",
  use: {
    baseURL,
    headless: true,
    screenshot: "only-on-failure",
    video: "off",
    trace: "off",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
