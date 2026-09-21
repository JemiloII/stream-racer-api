import type { FullConfig } from "@playwright/test";

// Runs once before the workers start. Sets SR_UI_SKIP_REASON (visible to every test through process.env)
// when the game is not answering, so the specs skip instead of failing on a dead server.
export const SKIP_ENV = "SR_UI_SKIP_REASON";

export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL || "http://127.0.0.1:8793";
  try {
    const response = await fetch(`${baseURL}/version`, { signal: AbortSignal.timeout(3000) });
    if (!response.ok) throw new Error(`GET /version answered ${response.status}`);
    const version = (await response.json()) as { api?: string; game?: string };
    console.log(`Stream Racer API ${version.api} (game ${version.game}) at ${baseURL}`);
    delete process.env[SKIP_ENV];
  } catch (error) {
    const reason = `Stream Racer is not running at ${baseURL} (${(error as Error).message}); UI tests skipped`;
    console.log(reason);
    process.env[SKIP_ENV] = reason;
  }
}
