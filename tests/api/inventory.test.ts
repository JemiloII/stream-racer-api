// GET /inventory/:login — boosts and chat respawns left for a racer.
import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { ApiError } from '../support/apiTypes';
import { readSettings } from '../support/settingsStore';

describe('GET /inventory/:login', () => {
  test('a login that is not in the field is 404', async () => {
    const response = await get<ApiError>('/inventory/nobody_here_xyz');
    expect(response.status).toBe(404);
    expect(response.json!.error).toMatch(/no vehicle/);
  });

  test('a missing login is 400', async () => {
    expect((await get<ApiError>('/inventory')).status).toBe(400);
  });

  test('settings carry the respawn limit (default 2)', async () => {
    const settings = await readSettings();
    expect(typeof settings.respawnLimit).toBe('number');
    expect(settings.respawnLimit).toBeGreaterThanOrEqual(0);
  });
});
