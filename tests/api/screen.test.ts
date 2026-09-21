import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import { knownScreens, type ScreenState } from '../support/apiTypes';

describe('GET /screen', () => {
  test('names a known screen and says whether a race or lobby is up', async () => {
    const response = await get<ScreenState>('/screen');
    expect(response.status).toBe(200);
    const screen = response.json!;
    expect(knownScreens, `unknown screen "${screen.screen}"`).toContain(screen.screen);
    expect(screen.scene).toEqual(expect.any(String));
    expect(screen.running).toEqual(expect.any(Boolean));
    expect(screen.lobby).toEqual(expect.any(Boolean));
    expect(screen.vehicles).toBeGreaterThanOrEqual(0);
  });

  test('running and lobby never both hold', async () => {
    const screen = (await get<ScreenState>('/screen')).json!;
    expect(screen.running && screen.lobby).toBe(false);
    if (screen.running) expect(screen.screen).toBe('racing');
    if (screen.lobby) expect(['lobby', 'postgame']).toContain(screen.screen);
  });
});
