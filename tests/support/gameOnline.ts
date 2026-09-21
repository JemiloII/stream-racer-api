import { apiBaseUrl, get } from './apiClient';

export interface GameProbe {
  online: boolean;
  reason: string;
}

/** Is the mod answering? 401 means it is up but wants a token (set SR_TOKEN). */
export async function probeGame(): Promise<GameProbe> {
  try {
    const version = await get('/version');
    if (version.status === 401) return { online: false, reason: `the API at ${apiBaseUrl} requires a token: set SR_TOKEN` };
    if (!version.ok) return { online: false, reason: `GET ${apiBaseUrl}/version answered ${version.status}` };
    return { online: true, reason: '' };
  } catch (error) {
    const cause = error instanceof Error && error.cause instanceof Error ? error.cause.message : error instanceof Error ? error.message : String(error);
    return { online: false, reason: `game not running (no API at ${apiBaseUrl}: ${cause})` };
  }
}

let cachedProbe: Promise<GameProbe> | undefined;

/** One probe per worker: the setup file and the test files in it share the answer. */
export function gameProbe(): Promise<GameProbe> {
  cachedProbe ??= probeGame();
  return cachedProbe;
}
