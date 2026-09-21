// Race story, part 1: from the home screen to a lobby with the three test cars in it.
import { beforeAll, describe, expect, test } from 'vitest';
import { post } from '../support/apiClient';
import type { Affected, ApiError, LobbyResult, RaceSnapshot } from '../support/apiTypes';
import { waitForEvent } from '../support/eventStream';
import { idleScreens, screenState, waitForScreen } from '../support/gameScreen';
import { sleep } from '../support/timing';
import { expectGameOn, kickOutsiders, raceSnapshot, testBotLogins, testBots, testField, vehicleOf } from './raceFixtures';

describe('lobby', () => {
  beforeAll(async () => {
    await expectGameOn(idleScreens, 'you: the race story starts from the home screen');
  });

  test('POST /race/start refuses with 409 while there is no lobby', async () => {
    const response = await post<ApiError>('/race/start');
    expect(response.status).toBe(409);
    expect(response.json!.error).toMatch(/no lobby/);
  });

  test('POST /lobby opens a lobby and the lobby event fires once it is joinable', async () => {
    // The lobby event fires after the scene reload, not when NewGame is called: listen before asking.
    const lobbyEvent = waitForEvent<RaceSnapshot>('lobby', 90_000);
    const response = await post<LobbyResult>('/lobby?map=locate');
    expect([200, 202], response.text).toContain(response.status);
    expect(response.json!.ok).toBe(true);
    const event = await lobbyEvent;
    expect(event, 'no lobby event within 90 s').not.toBeNull();
    expect(event!.data.lobby).toBe(true);
    expect(await waitForScreen('lobby', 30)).toBe('lobby');
    await sleep(1000);
  });

  test('cars that other clients auto-joined are kicked so the field is exactly the test cars', async () => {
    await sleep(2000); // give bots listening on /events a moment to join, then clear them
    const outsiders = await kickOutsiders();
    if (outsiders.length) console.log(`kicked auto-joined outsiders: ${outsiders.join(', ')}`);
    expect((await raceSnapshot()).vehicles).toHaveLength(0);
  });

  test('POST /lobby again is refused while a lobby is open', async () => {
    const response = await post<ApiError>('/lobby');
    expect(response.status).toBe(409);
    expect(response.json!.error).toMatch(/already in a lobby/);
  });

  test('POST /join adds the test cars', async () => {
    const response = await post<Affected>('/join', testBots);
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(testBots.length);
    const snapshot = await raceSnapshot();
    expect(snapshot.vehicles.map((vehicle) => vehicle.login).sort()).toEqual([...testBotLogins].sort());
  });

  test('joining the same logins again does not duplicate them', async () => {
    const response = await post<Affected>('/join', testBots);
    expect(response.json!.affected).toBe(0);
    expect((await raceSnapshot()).vehicles).toHaveLength(testBots.length);
    expect((await screenState()).vehicles).toBe(testBots.length);
  });

  test('the snapshot carries the map and each car keeps its color, auto-assigned when none was given', async () => {
    const snapshot = await raceSnapshot();
    expect(snapshot.lobby).toBe(true);
    expect(snapshot.running).toBe(false);
    expect(snapshot.map?.name, 'snapshot.map.name').toEqual(expect.any(String));
    expect(snapshot.map?.name.toLowerCase()).toContain('locate');
    expect(vehicleOf(snapshot, 'sr_test_b').color.toLowerCase()).toBe('#35e0ff');
    expect(vehicleOf(snapshot, 'sr_test_a').color).toMatch(/^#[0-9a-f]{6}$/i);
    expect(vehicleOf(snapshot, 'sr_test_c').color).toMatch(/^#[0-9a-f]{6}$/i);
    expect(vehicleOf(snapshot, 'sr_test_a').displayName).toBe('Test A');
    for (const vehicle of snapshot.vehicles) expect(vehicle.finished).toBe(false);
  });

  test('POST /kick/:login removes a car from the lobby and it can join again', async () => {
    const kicked = await post<Affected>('/kick/sr_test_c');
    expect(kicked.status, kicked.text).toBe(200);
    expect(kicked.json!.affected).toBe(1);
    expect((await raceSnapshot()).vehicles.map((vehicle) => vehicle.login)).not.toContain('sr_test_c');
    const rejoined = await post<Affected>('/join', testBots.filter((bot) => bot.login === 'sr_test_c'));
    expect(rejoined.json!.affected).toBe(1);
    expect((await raceSnapshot()).vehicles).toHaveLength(testBots.length);
  });

  test('POST /kick on a login that is not in the lobby is 404', async () => {
    const response = await post<ApiError>('/kick/nobody_here');
    expect(response.status).toBe(404);
  });
});
