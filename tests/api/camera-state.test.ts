import { describe, expect, test } from 'vitest';
import { get, post } from '../support/apiClient';
import type { ApiError, CameraState, ScreenState } from '../support/apiTypes';

describe('GET /camera', () => {
  test('reports the director flag, the shot and its targets', async () => {
    const response = await get<CameraState>('/camera');
    expect(response.status).toBe(200);
    const camera = response.json!;
    expect(camera.auto).toEqual(expect.any(Boolean));
    expect(camera).toHaveProperty('mode');
    expect(camera).toHaveProperty('target');
    expect(Array.isArray(camera.cars)).toBe(true);
    expect(camera.fov).toBeGreaterThan(0);
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
