import type { APIRequestContext } from "@playwright/test";

// Server truth the specs compare the DOM against. Shapes follow src/Settings.cs / Routes.cs (only what the UI shows).
export interface AutoJoinEntry { login: string; displayName: string; color: string | null; image: string | null }
export interface CustomBot { login: string; displayName: string; color: string | null; image: string | null }
export interface Webhook { event: string; url: string; method: string; enabled?: boolean }

export interface ServerSettings {
  autoJoinStreamer: boolean;
  autoJoin: AutoJoinEntry[];
  bots: string[];
  customBots: CustomBot[];
  botOptions: Record<string, { autoBoost?: boolean }>;
  webhooks: Webhook[];
  colorCommand: string;
  respawnCommand: string;
  minimap: { enabled: boolean; aspect: string; names: boolean; leaderBig: boolean };
  overlay: { names?: boolean; board?: number; boardSide?: "left" | "right"; boardScale?: number; showInLobby?: boolean };
  camera?: { shots?: Record<string, boolean> };
  ui: { cameraOnControls?: boolean };
  config: { port: number; hotkeyBoost?: string; tokenRequired: boolean };
}

export interface ServerVersion { api: string; commit: string; game: string; latest: string | null; upToDate: boolean | null }

export interface Vehicle { place: number; login: string; displayName: string; color: string; pct: number; finished: boolean; boosts: number }
export interface RaceSnapshot { running: boolean; lobby: boolean; streamer: string | null; vehicles: Vehicle[] }

export interface ServerTruth { settings: ServerSettings; version: ServerVersion; race: RaceSnapshot }

async function getJson<T>(request: APIRequestContext, path: string): Promise<T> {
  const response = await request.get(path, { headers: { Accept: "application/json" } });
  if (!response.ok()) throw new Error(`GET ${path} answered ${response.status()}`);
  return (await response.json()) as T;
}

export async function fetchServerTruth(request: APIRequestContext): Promise<ServerTruth> {
  const [settings, version, race] = await Promise.all([
    getJson<ServerSettings>(request, "/settings"),
    getJson<ServerVersion>(request, "/version"),
    getJson<RaceSnapshot>(request, "/race"),
  ]);
  return { settings, version, race };
}

// Bots are starred on the Bots page, so the Settings auto-join list only shows people who are not bots.
export function autoJoinPeople(settings: ServerSettings): AutoJoinEntry[] {
  const botLogins = new Set([...(settings.bots || []), ...(settings.customBots || []).map((bot) => bot.login)]);
  return (settings.autoJoin || []).filter((entry) => !botLogins.has(entry.login));
}
