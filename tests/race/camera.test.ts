// Race story, part 3: every camera shot while the cars are still driving, then the auto director on and off.
import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { get, post } from '../support/apiClient';
import type { Affected, ApiError, CameraState } from '../support/apiTypes';
import { gameProbe } from '../support/gameOnline';
import { pollUntil } from '../support/timing';
import { expectGameOn, raceSnapshot, testField } from './raceFixtures';

interface ShotCase {
  route: string;
  mode: string;
  targeted?: string;
}

const groupShots: ShotCase[] = [
  { route: '/camera/pack', mode: 'pack' },
  { route: '/camera/side', mode: 'side' },
  { route: '/camera/grid', mode: 'grid' },
  { route: '/camera/sweep', mode: 'sweep' },
  { route: '/camera/high', mode: 'high' },
  { route: '/camera/overhead', mode: 'overhead' },
  { route: '/camera/finish', mode: 'finish' },
  { route: '/camera/leader', mode: 'follow' },
];

const targetedShots: ShotCase[] = [
  { route: '/camera/chase/sr_test_a', mode: 'chase', targeted: 'sr_test_a' },
  { route: '/camera/front/sr_test_a', mode: 'front', targeted: 'sr_test_a' },
  { route: '/camera/orbit/sr_test_a', mode: 'orbit', targeted: 'sr_test_a' },
  { route: '/camera/wide/sr_test_a', mode: 'followwide', targeted: 'sr_test_a' },
  { route: '/camera/focus/sr_test_a', mode: 'follow', targeted: 'sr_test_a' },
];

describe('camera', () => {
  beforeAll(async () => {
    await expectGameOn('racing', 'racing.test.ts');
  });

  afterAll(async () => {
    if (!(await gameProbe()).online) return;
    await post('/camera/auto?on=0');
    await post('/camera/free');
  });

  test.each([...groupShots, ...targetedShots])('POST $route switches to the $mode shot', async ({ route, mode, targeted }) => {
    const response = await post<CameraState>(`${route}?seconds=0`);
    expect(response.status, `${route}: ${response.text}`).toBe(200);
    expect(response.json!.mode).toBe(mode);
    if (targeted) {
      expect(response.json!.target).toBe(targeted);
      expect(response.json!.cars).toContain(targeted);
    }
    expect((await get<CameraState>('/camera')).json!.mode).toBe(mode);
  });

  test('POST /camera/boom orbits the most recently boomed car', async () => {
    // pick a test car that can take a boom right now (driving, not already mid-boom from racing.test.ts)
    const victim = await pollUntil(
      async () => testField(await raceSnapshot()).find((vehicle) => vehicle.state === 'driving')?.login,
      (login) => login !== undefined,
      { attempts: 40, intervalMs: 500 },
    );
    const boomed = await post<Affected>(`/boom/${victim}`);
    expect(boomed.status, boomed.text).toBe(200);
    const response = await post<CameraState>('/camera/boom?seconds=0');
    expect(response.status, response.text).toBe(200);
    expect(response.json!.mode).toBe('orbit');
    expect(response.json!.target).toBe(victim);
  });

  test('POST /camera/prop picks a track camera, or says 409 when none is in range', async () => {
    const response = await post<CameraState | ApiError>('/camera/prop?seconds=0');
    expect([200, 409]).toContain(response.status);
    if (response.status === 200) expect((response.json as CameraState).mode).toBe('prop');
  });

  test('fov and fovTo are accepted and reported', async () => {
    const response = await post<CameraState>('/camera/high?seconds=0&fov=40&fovTo=40');
    expect(response.status).toBe(200);
    expect(response.json!.fov).toBeGreaterThan(0);
  });

  test('a targeted shot on an unknown car is 404', async () => {
    const response = await post<ApiError>('/camera/chase/nobody_here');
    expect(response.status).toBe(404);
    expect(response.json!.error).toMatch(/no vehicle/);
  });

  test('POST /camera/free releases the game free cam', async () => {
    const response = await post<CameraState>('/camera/free');
    expect(response.status).toBe(200);
    expect(response.json!.mode).toBe('free');
    expect(response.json!.auto).toBe(false);
  });

  test('POST /camera/auto?on=1 and ?on=0 set the director; no query toggles it', async () => {
    expect((await post<CameraState>('/camera/auto?on=1')).json!.auto).toBe(true);
    expect((await get<CameraState>('/camera')).json!.auto).toBe(true);
    expect((await post<CameraState>('/camera/auto?on=0')).json!.auto).toBe(false);
    expect((await post<CameraState>('/camera/auto')).json!.auto).toBe(true);
    expect((await post<CameraState>('/camera/auto')).json!.auto).toBe(false);
    expect((await get<CameraState>('/camera')).json!.auto).toBe(false);
  });
});
