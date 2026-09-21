import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { post } from '../support/apiClient';
import type { MinimapState } from '../support/apiTypes';
import { readSettings } from '../support/settingsStore';

describe('POST /minimap', () => {
  let originallyEnabled: boolean;

  beforeAll(async () => {
    originallyEnabled = (await readSettings()).minimap.enabled;
  });

  afterAll(async () => {
    await post(`/minimap?on=${originallyEnabled ? 1 : 0}`);
  });

  test('without a query it toggles, and the new state persists in settings', async () => {
    const toggled = await post<MinimapState>('/minimap');
    expect(toggled.status).toBe(200);
    expect(toggled.json!.enabled).toBe(!originallyEnabled);
    expect((await readSettings()).minimap.enabled).toBe(!originallyEnabled);
  });

  test('toggling again returns to where it started', async () => {
    const toggledBack = await post<MinimapState>('/minimap');
    expect(toggledBack.json!.enabled).toBe(originallyEnabled);
    expect((await readSettings()).minimap.enabled).toBe(originallyEnabled);
  });

  test('?on= sets an explicit state', async () => {
    expect((await post<MinimapState>('/minimap?on=1')).json!.enabled).toBe(true);
    expect((await post<MinimapState>('/minimap?on=0')).json!.enabled).toBe(false);
    expect((await post<MinimapState>('/minimap?on=true')).json!.enabled).toBe(true);
  });

  test('the response is the full mini map state the pages draw from', async () => {
    const state = (await post<MinimapState>(`/minimap?on=${originallyEnabled ? 1 : 0}`)).json!;
    expect(state.aspect).toMatch(/^\d+:\d+$/);
    expect(state.x).toEqual(expect.any(Number));
    expect(state.y).toEqual(expect.any(Number));
    expect(state.w).toBeGreaterThan(0);
    expect(state.bg).toMatch(/^#[0-9a-f]{6}$/i);
    expect(state.live).toEqual(expect.any(Boolean));
  });
});
