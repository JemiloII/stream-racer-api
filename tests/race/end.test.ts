// Race story, part 4: end the race, check the postgame lock-out, and move on to a clean lobby.
import { beforeAll, describe, expect, test } from 'vitest';
import { post } from '../support/apiClient';
import type { Affected, ApiError, RaceSnapshot } from '../support/apiTypes';
import { waitForEvent } from '../support/eventStream';
import { screenState, waitForScreen } from '../support/gameScreen';
import { sleep } from '../support/timing';
import { expectGameOn, kickOutsiders, raceSnapshot, testBots, testField } from './raceFixtures';

describe('end of race', () => {
  beforeAll(async () => {
    await expectGameOn('racing', 'racing.test.ts');
  });

  test('POST /race/end fires race_end with a snapshot and the game lands on postgame', async () => {
    const raceEnd = waitForEvent<RaceSnapshot>('race_end', 10_000);
    const response = await post<Affected>('/race/end');
    expect(response.status, response.text).toBe(200);
    const event = await raceEnd;
    expect(event, 'no race_end event within 10 s').not.toBeNull();
    expect(event!.data.vehicles.map((vehicle) => vehicle.login).sort()).toEqual(testBots.map((bot) => bot.login).sort());
    expect(await waitForScreen('postgame', 20)).toBe('postgame');
    const state = await screenState();
    expect(state.running).toBe(false);
  });

  test('joining is refused in postgame: the old cars are still in the scene', async () => {
    const join = await post<ApiError>('/join', testBots);
    expect(join.status).toBe(409);
    expect(join.json!.error).toMatch(/race over/);
    expect((await post<ApiError>('/join/me')).status).toBe(409);
  });

  test('POST /race/start is refused in postgame until /race/next', async () => {
    const response = await post<ApiError>('/race/start');
    expect(response.status).toBe(409);
    expect(response.json!.error).toMatch(/race over/);
  });

  test('POST /race/next opens the next queued map as a lobby with zero cars', async () => {
    const lobbyEvent = waitForEvent<RaceSnapshot>('lobby', 90_000);
    const response = await post<Affected>('/race/next');
    expect(response.status, response.text).toBe(200);
    const event = await lobbyEvent;
    expect(event, 'no lobby event within 90 s').not.toBeNull();
    expect(await waitForScreen('lobby', 30)).toBe('lobby');
    await sleep(1000);
    const snapshot = await raceSnapshot();
    expect(snapshot.lobby).toBe(true);
    expect(snapshot.running).toBe(false);
    expect(testField(snapshot), 'old cars must not carry over into the next lobby').toHaveLength(0);
    expect(snapshot.map?.name).toEqual(expect.any(String));
    await sleep(2000);
    await kickOutsiders(); // bots listening on /events join the new lobby too
    expect((await raceSnapshot()).vehicles).toHaveLength(0);
  });

  test('the new lobby accepts joins again', async () => {
    const response = await post<Affected>('/join', testBots.slice(0, 1));
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
    const kicked = await post<Affected>('/kick/sr_test_a');
    expect(kicked.json!.affected).toBe(1);
    expect(testField(await raceSnapshot())).toHaveLength(0);
  });

  test('POST /lobby/exit hands the game back on the home screen', async () => {
    const response = await post<Affected>('/lobby/exit');
    expect(response.status, response.text).toBe(200);
    expect(['home', 'main', 'play']).toContain(await waitForScreen(['home', 'main', 'play'], 30));
  });
});
