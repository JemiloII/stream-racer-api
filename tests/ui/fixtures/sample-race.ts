// A small field the overlay specs feed through the fake event stream, shaped like GET /race and the snapshot events.
export interface SampleVehicle { place: number; id: string; login: string; displayName: string; color: string; sub: boolean; pct: number; finished: boolean; boosts: number; avatar: string | null; x: number; z: number }
export interface SampleSnapshot { running: boolean; lobby: boolean; streamer: string; vehicles: SampleVehicle[] }

const FIELD: SampleVehicle[] = [
  { place: 1, id: "1", login: "alpha_racer", displayName: "Alpha", color: "#ff8a00", sub: false, pct: 61.2, finished: false, boosts: 2, avatar: null, x: 10, z: -20 },
  { place: 2, id: "2", login: "bravo_racer", displayName: "Bravo", color: "#35e0ff", sub: true, pct: 44.9, finished: false, boosts: 1, avatar: null, x: 8, z: -18 },
  { place: 3, id: "3", login: "charlie_racer", displayName: "Charlie", color: "#3ddc84", sub: false, pct: 12.0, finished: false, boosts: 0, avatar: null, x: 2, z: -5 },
];
const field = () => FIELD.map((vehicle) => ({ ...vehicle }));

export const SAMPLE_LOGINS = FIELD.map((vehicle) => vehicle.login);
export const racingSnapshot = (): SampleSnapshot => ({ running: true, lobby: false, streamer: "alpha_racer", vehicles: field() });
export const lobbySnapshot = (): SampleSnapshot => ({ running: false, lobby: true, streamer: "alpha_racer", vehicles: field().map((vehicle) => ({ ...vehicle, pct: 0 })) });
export const endedSnapshot = (): SampleSnapshot => ({ running: false, lobby: false, streamer: "alpha_racer", vehicles: field().map((vehicle) => ({ ...vehicle, pct: 100, finished: true })) });
/** Post-game / home screen: the cars are still in the scene but neither a race nor a lobby is on. */
export const idleSnapshot = (): SampleSnapshot => ({ ...endedSnapshot() });
