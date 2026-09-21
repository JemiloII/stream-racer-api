import { get } from './apiClient';
import type { ScreenState } from './apiTypes';
import { sleep } from './timing';

/** The menu reports "home" ("main" on older builds) when nothing is going on. */
export const idleScreens: readonly string[] = ['home', 'main'];

export async function screenState(): Promise<ScreenState> {
  const response = await get<ScreenState>('/screen');
  if (!response.json) throw new Error(`GET /screen answered ${response.status}: ${response.text}`);
  return response.json;
}

export async function currentScreen(): Promise<string> {
  return (await screenState()).screen;
}

/** Poll /screen twice a second until it reports one of `wanted`; throws with the last screen seen on timeout. */
export async function waitForScreen(wanted: string | readonly string[], seconds: number): Promise<string> {
  const wantedScreens = typeof wanted === 'string' ? [wanted] : wanted;
  let lastSeen = '';
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    lastSeen = await currentScreen();
    if (wantedScreens.includes(lastSeen)) return lastSeen;
    await sleep(500);
  }
  throw new Error(`timed out after ${seconds}s waiting for screen ${wantedScreens.join('|')}; last seen "${lastSeen}"`);
}
