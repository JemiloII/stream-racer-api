// Runs once per `vitest run` (main process). The race files take over the game, so before they start:
//   - the game must be idle on the home screen (a lobby or race in progress would be hijacked),
//   - auto-join is switched off so only the test cars are in the field,
// and the touched settings are restored when the run ends, pass or fail.
import type { TestProject } from 'vitest/node';
import { probeGame } from '../support/gameOnline';
import { currentScreen, idleScreens } from '../support/gameScreen';
import { readSettings, writeSettings } from '../support/settingsStore';

export default async function raceGlobalSetup(project: TestProject): Promise<(() => Promise<void>) | undefined> {
  const scanDir = (project.config.dir ?? project.config.root).replace(/\\/g, '/');
  if (scanDir.endsWith('/tests/api')) return undefined; // read-only run: nothing to guard

  const probe = await probeGame();
  if (!probe.online) return undefined; // the setup file skips every test with the reason

  const screen = await currentScreen();
  if (!idleScreens.includes(screen)) {
    throw new Error(`race tests take over the game and need it idle on the home screen, but it is on "${screen}". Back out to the menu and re-run.`);
  }

  const saved = await readSettings();
  await writeSettings({ autoJoin: [], autoJoinStreamer: false });

  return async () => {
    await writeSettings({ autoJoin: saved.autoJoin, autoJoinStreamer: saved.autoJoinStreamer, botOptions: saved.botOptions });
  };
}
