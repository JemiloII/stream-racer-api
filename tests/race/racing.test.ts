// Race story, part 2: start the race and exercise everything that only works while cars are driving.
import { beforeAll, describe, expect, test } from 'vitest';
import { get, post } from '../support/apiClient';
import type { Affected, ApiError, BoostsEvent, BoostUseResult, PerkEvent, PerksInfo, PosFrame, RaceSnapshot, TrackResponse, Vehicle, ZonesResponse } from '../support/apiTypes';
import { collectEvents, waitForEvent } from '../support/eventStream';
import { gameProbe } from '../support/gameOnline';
import { waitForScreen } from '../support/gameScreen';
import { readSettings } from '../support/settingsStore';
import { pollUntil } from '../support/timing';
import { expectGameOn, raceSnapshot, testBotLogins, testBots, vehicleOf, waitUntilDriving } from './raceFixtures';

describe('racing', () => {
  beforeAll(async () => {
    await expectGameOn('lobby', 'lobby.test.ts');
    if (!(await gameProbe()).online) return;
    const fieldSize = (await raceSnapshot()).vehicles.length;
    if (fieldSize === 0) throw new Error('the lobby is empty: lobby.test.ts must have joined the test cars first');
  });

  test('POST /race/start?now=1 starts immediately and race_start fires, followed by one boosts event per car', async () => {
    const raceStart = waitForEvent<RaceSnapshot>('race_start', 30_000);
    const pools = collectEvents<BoostsEvent>({ durationMs: 5000, events: ['boosts'] });
    const response = await post<Affected>('/race/start?now=1');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
    expect(await raceStart, 'no race_start event within 30 s').not.toBeNull();
    expect(await waitForScreen('racing', 30)).toBe('racing');
    const resets = (await pools).map((event) => event.data);
    for (const login of testBotLogins) {
      const reset = resets.find((event) => event.login === login);
      expect(reset, `boosts event for ${login} at race start`).toBeDefined();
      expect(reset!.boosts).toEqual(expect.any(Number));
      expect(reset!.delta).toBe(0);
    }
  });

  test('GET /perks/:login for a car in the race: test bots are nobody special, so 0 extra boosts and a reason', async () => {
    const perks = (await get<PerksInfo>('/perks/sr_test_a')).json!;
    expect(perks.inRace).toBe(true);
    expect(perks.granted, 'GrantPerks ran for the car (after the backend title arrived)').toBe(true);
    expect(perks.subscriber).toBe(false);
    expect(perks.host).toBe(false);
    expect(perks.boosts).toEqual(expect.any(Number));
    if (!perks.follower) {
      expect(perks.extraBoosts).toBe(0);
      expect(perks.why.length).toBeGreaterThan(0);
    }
  });

  test('pos frames stream at the configured rate as [login, x, z, pct, place, finished, state]', async () => {
    const config = (await readSettings()).config;
    const listenSeconds = 3;
    const events = await collectEvents<PosFrame | RaceSnapshot>({ durationMs: listenSeconds * 1000, events: ['pos', 'positions'] });
    const posFrames = events.filter((event) => event.event === 'pos').map((event) => event.data as PosFrame);
    const snapshots = events.filter((event) => event.event === 'positions').map((event) => event.data as RaceSnapshot);

    // pos is emitted from Unity's Update, so the real rate is min(posHz, the game's frame rate). Ask for a third of nominal.
    const nominalFrames = config.posHz * listenSeconds;
    expect(posFrames.length, `expected about ${nominalFrames} pos frames in ${listenSeconds} s, got ${posFrames.length}`).toBeGreaterThanOrEqual(nominalFrames / 3);
    expect(snapshots.length, 'positions snapshots at tickHz').toBeGreaterThanOrEqual(Math.floor((config.tickHz * listenSeconds) / 2));

    const firstFrame = posFrames[0]!;
    expect(firstFrame.t).toEqual(expect.any(Number));
    expect(firstFrame.v).toHaveLength(testBots.length);
    for (const car of firstFrame.v) {
      expect(car).toHaveLength(7);
      const [login, x, z, pct, place, finished, state] = car;
      expect(testBotLogins).toContain(login);
      expect(x).toEqual(expect.any(Number));
      expect(z).toEqual(expect.any(Number));
      expect(pct).toBeGreaterThanOrEqual(0);
      expect(pct).toBeLessThanOrEqual(100);
      expect(place).toBeGreaterThanOrEqual(1);
      expect(place).toBeLessThanOrEqual(testBots.length);
      expect([0, 1]).toContain(finished);
      expect(state).toEqual(expect.any(String));
    }
    expect(new Set(firstFrame.v.map((car) => car[4])).size, 'places are unique').toBe(testBots.length);
    const timestamps = posFrames.map((frame) => frame.t);
    expect(timestamps).toEqual([...timestamps].sort((left, right) => left - right));

    expect(snapshots[0]!.running).toBe(true);
    expect(snapshots[0]!.vehicles).toHaveLength(testBots.length);
  });

  test('GET /track/zones lists straights that all end at or before the finish (retry: the circuit can lag race_start)', async () => {
    const response = await pollUntil(
      () => get<ZonesResponse>('/track/zones'),
      (candidate) => (candidate.json?.zones.length ?? 0) > 0,
      { attempts: 8, intervalMs: 1500 },
    );
    expect(response.status).toBe(200);
    const zones = response.json!;
    expect(zones.zones.length, 'boost zones for the map').toBeGreaterThan(0);
    expect(zones.mapName).toEqual(expect.any(String));
    expect(zones.length).toBeGreaterThan(0);
    expect(zones.finishAt).toBeGreaterThan(0);
    for (const zone of zones.zones) {
      expect(zone.start).toBeGreaterThanOrEqual(0);
      expect(zone.end).toBeGreaterThan(zone.start);
      expect(zone.end, `zone ${zone.start}-${zone.end} runs past finishAt ${zones.finishAt}`).toBeLessThanOrEqual(zones.finishAt + 1);
    }
  });

  test('GET /track has the route outline and bounds', async () => {
    const track = (await get<TrackResponse>('/track')).json!;
    expect(track.points.length).toBeGreaterThan(10);
    expect(track.bounds).not.toBeNull();
    expect(track.bounds!.maxX).toBeGreaterThan(track.bounds!.minX);
    expect(track.bounds!.maxZ).toBeGreaterThan(track.bounds!.minZ);
    const snapshot = await raceSnapshot();
    for (const vehicle of snapshot.vehicles) {
      expect(vehicle.x).toBeGreaterThanOrEqual(track.bounds!.minX - 50);
      expect(vehicle.x).toBeLessThanOrEqual(track.bounds!.maxX + 50);
    }
  });

  test('POST /boost/:login/use spends the pool one by one, each spend fires a boost event with boosts left, and is 409 once empty', async () => {
    await waitUntilDriving('sr_test_a');
    const startingBoosts = vehicleOf(await raceSnapshot(), 'sr_test_a').boosts;
    expect(startingBoosts, 'a fresh car has boosts to spend').toBeGreaterThan(0);
    const boostEvent = waitForEvent<Vehicle>('boost', 5000);
    const firstUse = await post<BoostUseResult>('/boost/sr_test_a/use');
    expect(firstUse.status, firstUse.text).toBe(200);
    expect(firstUse.json!.affected).toBe(1);
    expect(firstUse.json!.boosts).toBe(startingBoosts - 1);
    const spent = await boostEvent;
    expect(spent, 'no boost event for the pool spend').not.toBeNull();
    expect(spent!.data.login).toBe('sr_test_a');
    expect(spent!.data.boosts, 'the boost event carries the pool after the spend').toBe(startingBoosts - 1);

    let remaining = firstUse.json!.boosts;
    while (remaining > 0) {
      const nextUse = await post<BoostUseResult>('/boost/sr_test_a/use');
      expect(nextUse.status, nextUse.text).toBe(200);
      expect(nextUse.json!.boosts).toBe(remaining - 1);
      remaining = nextUse.json!.boosts;
    }
    const emptyPool = await post<ApiError & { boosts: number }>('/boost/sr_test_a/use');
    expect(emptyPool.status).toBe(409);
    expect(emptyPool.json!.error).toMatch(/no boosts/);
    expect(emptyPool.json!.boosts).toBe(0);
  });

  test('POST /boost/:login/add?n=2 refills the pool, answers the new count and fires a boosts event', async () => {
    const poolEvent = waitForEvent<BoostsEvent>('boosts', 5000);
    const response = await post<BoostUseResult>('/boost/sr_test_a/add?n=2');
    expect(response.status).toBe(200);
    expect(response.json!.affected).toBe(1);
    expect(response.json!.boosts).toBe(2);
    expect(vehicleOf(await raceSnapshot(), 'sr_test_a').boosts).toBe(2);
    const added = await poolEvent;
    expect(added, 'no boosts event for the add').not.toBeNull();
    expect(added!.data.login).toBe('sr_test_a');
    expect(added!.data.boosts).toBe(2);
    expect(added!.data.delta).toBe(2);
  });

  test('POST /boost/all/add?n=1 tops up every car (affected = field size)', async () => {
    const before = Object.fromEntries((await raceSnapshot()).vehicles.map((vehicle) => [vehicle.login, vehicle.boosts]));
    const response = await post<Affected>('/boost/all/add?n=1');
    expect(response.status).toBe(200);
    expect(response.json!.affected).toBe(testBots.length);
    for (const vehicle of (await raceSnapshot()).vehicles) expect(vehicle.boosts, vehicle.login).toBe(before[vehicle.login]! + 1);
  });

  test('perk events (if any arrived this race) say why and whether follower checks could run', async () => {
    const perks = await collectEvents<PerkEvent>({ durationMs: 500, events: ['perk'] });
    for (const event of perks) {
      expect(event.data.extraBoosts).toEqual(expect.any(Number));
      expect(Array.isArray(event.data.reasons)).toBe(true);
      expect(['ok', 'no token', 'unknown']).toContain(event.data.followerChecks);
    }
  });

  test('POST /boost/:login fires a free boost without touching the pool', async () => {
    await waitUntilDriving('sr_test_a');
    const poolBefore = vehicleOf(await raceSnapshot(), 'sr_test_a').boosts;
    const response = await post<Affected>('/boost/sr_test_a?seconds=1');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
    expect(vehicleOf(await raceSnapshot(), 'sr_test_a').boosts).toBe(poolBefore);
  });

  test('POST /boom/:login stuns the car and emits a boom event for it', async () => {
    await waitUntilDriving('sr_test_b');
    const boomEvent = waitForEvent<{ login: string }>('boom', 5000);
    const response = await post<Affected>('/boom/sr_test_b');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
    const event = await boomEvent;
    expect(event, 'no boom event').not.toBeNull();
    expect(event!.data.login).toBe('sr_test_b');
    expect(vehicleOf(await raceSnapshot(), 'sr_test_b').state).toBe('stunned');
  });

  test('a car mid-boom cannot be boomed again (409)', async () => {
    const response = await post<ApiError>('/boom/sr_test_b');
    expect(response.status).toBe(409);
  });

  test('POST /boom/:n on a random car reports the real hits', async () => {
    const response = await post<Affected>('/boom/1');
    expect(response.status).toBe(200);
    expect(response.json!.affected).toBeGreaterThanOrEqual(0);
    expect(response.json!.affected).toBeLessThanOrEqual(1);
  });

  test('POST /respawn/:login triggers the stuck-car respawn', async () => {
    const response = await post<Affected>('/respawn/sr_test_c');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
  });

  test('POST /speed/all applies a top-speed multiplier to every car', async () => {
    const response = await post<Affected>('/speed/all?mult=0.5&seconds=2');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(testBots.length);
  });

  test('joining is refused mid-race with 409', async () => {
    const join = await post<ApiError>('/join', testBots);
    expect(join.status).toBe(409);
    expect(join.json!.error).toMatch(/race running/);
    expect((await post<ApiError>('/join/me')).status).toBe(409);
    expect((await post<ApiError>('/autojoin/join')).status).toBe(409);
    expect((await raceSnapshot()).vehicles).toHaveLength(testBots.length);
  });

  test('an unknown car is 404 on every targeted route', async () => {
    for (const path of ['/boom/nobody_here', '/boost/nobody_here/use', '/respawn/nobody_here', '/finish/nobody_here']) {
      const response = await post<ApiError>(path);
      expect(response.status, path).toBe(404);
      expect(response.json!.error).toMatch(/no vehicle/);
    }
  });

  test('POST /finish/:login marks the car finished; a second call is 409', async () => {
    const response = await post<Affected>('/finish/sr_test_c');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.affected).toBe(1);
    const finished = vehicleOf(await raceSnapshot(), 'sr_test_c');
    expect(finished.finished).toBe(true);
    expect(finished.state).toBe('finished');
    expect(finished.pct).toBe(100);
    expect(finished.place).toBe(1);
    const again = await post<ApiError>('/finish/sr_test_c');
    expect(again.status).toBe(409);
  });
});
