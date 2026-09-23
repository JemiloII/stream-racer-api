import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { ScreenState, TrackResponse, ZonesResponse } from '../support/apiTypes';

// Outside a race there is no circuit loaded; both routes must still answer 200 with an empty, well-formed body.
// The populated shapes are covered by tests/race/racing.test.ts.
describe('GET /track', () => {
  test('answers an outline with points and bounds (empty when no track is loaded)', async () => {
    const response = await get<TrackResponse>('/track');
    expect(response.status).toBe(200);
    const track = response.json!;
    expect(Array.isArray(track.points)).toBe(true);
    for (const point of track.points) {
      expect(point).toHaveLength(2);
      expect(point[0]).toEqual(expect.any(Number));
      expect(point[1]).toEqual(expect.any(Number));
    }
    if (track.points.length === 0) expect(track.bounds).toBeNull();
    else expect(track.bounds).toEqual({ minX: expect.any(Number), maxX: expect.any(Number), minZ: expect.any(Number), maxZ: expect.any(Number) });
  });

  test('has no points while the game sits in the menu', async () => {
    const screen = (await get<ScreenState>('/screen')).json!;
    // the track stays loaded on the results screen too: only the menu is really "no track"
    if (!['home', 'main', 'play', 'settings'].includes(screen.screen)) return;
    expect((await get<TrackResponse>('/track')).json!.points).toEqual([]);
  });
});

describe('GET /track/zones', () => {
  test('answers the boost-zone shape with a zone list', async () => {
    const response = await get<ZonesResponse>('/track/zones');
    expect(response.status).toBe(200);
    const zones = response.json!;
    expect(zones).toHaveProperty('map');
    expect(zones).toHaveProperty('mapName');
    expect(zones.length).toBeGreaterThanOrEqual(0);
    expect(zones.finishAt).toBeGreaterThanOrEqual(0);
    expect(Array.isArray(zones.zones)).toBe(true);
    for (const zone of zones.zones) {
      expect(zone.end).toBeGreaterThan(zone.start);
      expect(zone.length).toBeCloseTo(zone.end - zone.start, 3);
    }
  });

  test('is empty while the game sits in the menu', async () => {
    const screen = (await get<ScreenState>('/screen')).json!;
    // the track stays loaded on the results screen too: only the menu is really "no track"
    if (!['home', 'main', 'play', 'settings'].includes(screen.screen)) return;
    const zones = (await get<ZonesResponse>('/track/zones')).json!;
    expect(zones.zones).toEqual([]);
    expect(zones.map).toBeNull();
    expect(zones.length).toBe(0);
  });
});
