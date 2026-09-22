// Response shapes as documented in ui/pages/api.js and README.md. Only the fields the tests rely on are typed.

export const knownScreens: readonly string[] = ['home', 'main', 'play', 'settings', 'lobby', 'racing', 'postgame', 'trackbuilder'];

export interface ScreenState {
  screen: string;
  scene: string;
  running: boolean;
  lobby: boolean;
  vehicles: number;
}

export interface VersionInfo {
  api: string;
  commit: string;
  game: string;
  unity: string;
  bepinex: string;
  latest: string | null;
  upToDate: boolean | null;
  updateUrl: string | null;
  developer: string;
  twitch: string;
}

export interface Vehicle {
  place: number;
  id: string;
  login: string;
  displayName: string;
  color: string;
  sub: boolean;
  type: string;
  progress: number;
  finishAt: number;
  pct: number;
  finished: boolean;
  boosts: number;
  state: string;
  image: string | null;
  avatar: string | null;
  title: string | null;
  /** World position; null while the car has no game object yet (lobby, state "spawning"). */
  x: number | null;
  z: number | null;
}

export interface RaceSnapshot {
  running: boolean;
  lobby: boolean;
  streamer: string | null;
  map: { id: number; name: string } | null;
  vehicles: Vehicle[];
}

export interface Affected {
  ok: boolean;
  affected: number;
}

export interface ApiError {
  error: string;
  [extra: string]: unknown;
}

export interface BoostUseResult extends Affected {
  boosts: number;
}

export const shotKeys = ['grid', 'high', 'side', 'sweep', 'pack', 'front', 'chase', 'orbit', 'overhead', 'prop', 'finish', 'duel', 'pileup', 'boom'] as const;
export type ShotKey = (typeof shotKeys)[number];

export interface CameraState {
  auto: boolean;
  mode: string | null;
  target: string | null;
  cars: string[];
  fov: number;
  /** Director toggles (settings.camera.shots); manual shots ignore them. */
  shots: Record<ShotKey, boolean>;
}

export type FollowerChecks = 'ok' | 'no token' | 'unknown';

/** GET /perks/:login */
export interface PerksInfo {
  login: string;
  inRace: boolean;
  follower: boolean;
  followerKnown: boolean;
  subscriber: boolean;
  developer: boolean;
  host: boolean;
  extraBoosts: number;
  why: string[];
  followerChecks: FollowerChecks;
  followerChecksError: string | null;
  granted: boolean;
  boosts: number | null;
  perks: Perks;
}

/** GET /chat */
export interface ChatStatus {
  connected: boolean;
  channel: string | null;
  login: string | null;
  replies: boolean;
  canSend: boolean | null;
  scopes: string[] | null;
  scopesError: string | null;
  note: string | null;
}

/** `boosts` SSE event: a pool changed without a boost being fired (add, perk, race start). */
export interface BoostsEvent {
  login: string;
  displayName: string;
  boosts: number;
  delta: number;
}

/** `perk` SSE event: extra boosts decided for a joined car (also fired with 0 so the reason is visible). */
export interface PerkEvent {
  login: string;
  displayName: string;
  extraBoosts: number;
  reasons: string[];
  boosts: number;
  followerChecks: FollowerChecks;
  follower: boolean;
}

export interface MinimapState {
  enabled: boolean;
  x: number;
  y: number;
  w: number;
  h: number;
  marker: number;
  bg: string;
  alpha: number;
  track: string;
  pad: number;
  leaderBig: boolean;
  names: boolean;
  aspect: string;
  live: boolean;
}

export interface PluginConfig {
  port: number;
  tokenRequired: boolean;
  bindAll: boolean;
  tickHz: number;
  posHz: number;
  hotkeyBoost: string;
  camUp: string;
  camDown: string;
}

export interface ConfigResult {
  ok: boolean;
  errors: string[];
  restarting: boolean;
  url: string;
  config: PluginConfig;
}

export interface RosterEntry {
  id: string;
  login: string;
  displayName?: string | null;
  color?: string | null;
  sub?: boolean;
  image?: string | null;
}

export interface Webhook {
  event: string;
  url: string;
  method: string;
  header: string;
  body: string;
  enabled: boolean;
}

export interface Perks {
  colorCommand: string;
  coloredNames: string;
  boostFollower: number;
  boostSubscriber: number;
  boostDeveloper: number;
  boostHost: number;
}

export interface SettingsDocument {
  autoJoinStreamer: boolean;
  streamerColor: string;
  autoJoin: RosterEntry[];
  customBots: RosterEntry[];
  colorLeaderboard: boolean;
  colorCommandEnabled: boolean;
  colorCommand: string;
  respawnCommandEnabled: boolean;
  respawnCommand: string;
  respawnLimit: number;
  colors: Record<string, string>;
  perks: Perks;
  botOptions: Record<string, { autoBoost: boolean }>;
  webhooks: Webhook[];
  twitchClientId: string;
  twitchTokenSet: boolean;
  twitch?: { connected: boolean; login: string; scopes: string[]; features: { followerChecks: boolean; chatReplies: boolean }; missing: string[] };
  /** Why follower perks may not apply: ok | no token | unknown (token set but the last Helix lookup failed). */
  followerChecks: FollowerChecks;
  followerChecksError: string | null;
  chatReplies: boolean;
  camera: { shots: Record<ShotKey, boolean> };
  ui: Record<string, unknown>;
  overlay: Record<string, unknown>;
  minimap: MinimapState;
  bots: string[];
  config: PluginConfig;
}

export interface MapInfo {
  id: number;
  name: string;
  creator: string;
  official: boolean;
  length: string;
  avgTime: number;
}

export interface MapsResponse {
  loading: boolean;
  maps: MapInfo[];
}

export interface TwitchUser {
  id: string;
  login: string;
  displayName: string;
  image: string;
  description: string;
}

export interface TwitchUsersResponse {
  users: TwitchUser[];
}

export interface StreamerInfo {
  id: string;
  login: string;
  inRace: boolean;
}

export interface BoostZone {
  start: number;
  end: number;
  length: number;
}

export interface ZonesResponse {
  map: number | null;
  mapName: string | null;
  length: number;
  finishAt: number;
  zones: BoostZone[];
}

export interface TrackResponse {
  points: [number, number][];
  bounds: { minX: number; maxX: number; minZ: number; maxZ: number } | null;
}

export interface LobbyResult {
  ok: boolean;
  state: string;
}

/** One car in a `pos` SSE frame: [login, x, z, pct, place, finished (0|1), state]. */
export type PosFrameCar = [string, number, number, number, number, 0 | 1, string];

export interface PosFrame {
  t: number;
  v: PosFrameCar[];
}
