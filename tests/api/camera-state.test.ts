import { beforeEach, describe, expect, test } from 'vitest';
import { get, post } from '../support/apiClient';
import { shotKeys, type ApiError, type CameraState, type ScreenState, type SettingsDocument } from '../support/apiTypes';
import { installedFeatures, missingFeatureReason } from '../support/installedFeatures';

describe('GET /camera', () => {
  test('reports the director flag, the shot and its targets', async () => {
    const response = await get<CameraState>('/camera');
    expect(response.status).toBe(200);
    const camera = response.json!;
    expect(camera.auto).toEqual(expect.any(Boolean));
    expect(camera).toHaveProperty('mode');
    expect(camera).toHaveProperty('target');
    expect(Array.isArray(camera.cars)).toBe(true);
    // fov comes from Camera.main, which only exists once the Play scene has a camera: the lobby reports 0.
    const screen = (await get<ScreenState>('/screen')).json!;
    if (screen.running) expect(camera.fov).toBeGreaterThan(0);
    else expect(camera.fov).toBeGreaterThanOrEqual(0);
  });
});

describe('GET /camera shot toggles', () => {
  beforeEach(async (context) => {
    if (!(await installedFeatures()).has('camera')) context.skip(missingFeatureReason('camera'));
  });

  test('lists every director shot with a boolean, the same object as settings.camera.shots', async () => {
    const camera = (await get<CameraState>('/camera')).json!;
    for (const key of shotKeys) expect(camera.shots[key], `shots.${key}`).toEqual(expect.any(Boolean));
    const settings = (await get<SettingsDocument>('/settings')).json!;
    expect(camera.shots).toEqual(settings.camera.shots);
  });
});

describe('POST /camera/<shot> outside a race', () => {
  test('refuses with 409 because there is nothing to film', async () => {
    const screen = (await get<ScreenState>('/screen')).json!;
    if (screen.running) return; // shots during a race are covered by tests/race/camera.test.ts
    const response = await post<ApiError>('/camera/pack?seconds=0');
    expect(response.status).toBe(409);
    expect(response.json!.error).toMatch(/no race/);
  });

  test('an unknown shot is 404', async () => {
    const response = await post<ApiError>('/camera/not-a-shot');
    expect(response.status).toBe(404);
  });
});
