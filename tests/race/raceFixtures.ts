import { get, post } from '../support/apiClient';
import type { RaceSnapshot, RosterEntry } from '../support/apiTypes';
import { gameProbe } from '../support/gameOnline';
import { screenState } from '../support/gameScreen';

export interface TestBot extends RosterEntry {
  /** false = the mod never spends this car's boosts, so the boost-pool tests see exactly what they put in. */
  autoBoost: boolean;
}

export const testBots: TestBot[] = [
  { id: '', login: 'sr_test_a', displayName: 'Test A', color: null, sub: false, autoBoost: false },
  { id: '', login: 'sr_test_b', displayName: 'Test B', color: '#35e0ff', sub: false, autoBoost: false },
  { id: '', login: 'sr_test_c', displayName: 'Test C', color: null, sub: false, autoBoost: false },
];

export const testBotLogins = testBots.map((bot) => bot.login);

/** Only the cars this story joined: anything else came from outside (a bot listening on /events). */
export function testField(snapshot: RaceSnapshot) {
  return snapshot.vehicles.filter((vehicle) => testBotLogins.includes(vehicle.login));
}

/** Kick every car that is not one of the test bots. Other clients auto-join lobbies (TTS-chan's bot does), which would skew counts. */
export async function kickOutsiders(): Promise<string[]> {
  const outsiders = (await raceSnapshot()).vehicles.filter((vehicle) => !testBotLogins.includes(vehicle.login)).map((vehicle) => vehicle.login);
  for (const login of outsiders) await post(`/kick/${encodeURIComponent(login)}`);
  return outsiders;
}

/** Boosts, free boosts and the like only work on a car that is actually driving (not on the grid, in the air, stunned). */
export async function waitUntilDriving(login: string, seconds = 30): Promise<void> {
  for (let attempt = 0; attempt < seconds * 2; attempt += 1) {
    const vehicle = testField(await raceSnapshot()).find((candidate) => candidate.login === login);
    if (vehicle?.state === 'driving') return;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${login} never reached the "driving" state within ${seconds} s`);
}

export async function raceSnapshot(): Promise<RaceSnapshot> {
  const response = await get<RaceSnapshot>('/race');
  if (!response.json) throw new Error(`GET /race answered ${response.status}: ${response.text}`);
  return response.json;
}

export function vehicleOf(snapshot: RaceSnapshot, login: string) {
  const vehicle = snapshot.vehicles.find((candidate) => candidate.login === login);
  if (!vehicle) throw new Error(`${login} is not in the field: ${snapshot.vehicles.map((candidate) => candidate.login).join(', ') || 'empty'}`);
  return vehicle;
}

/** Each race file picks up where the previous one left the game; fail loudly when the story is out of order. */
export async function expectGameOn(screens: string | readonly string[], leftThereBy: string): Promise<void> {
  if (!(await gameProbe()).online) return; // every test is about to be skipped by the setup file
  const wanted = typeof screens === 'string' ? [screens] : screens;
  const state = await screenState();
  if (!wanted.includes(state.screen)) throw new Error(`expected the game on "${wanted.join('|')}" (left there by ${leftThereBy}) but it is on "${state.screen}"`);
}
