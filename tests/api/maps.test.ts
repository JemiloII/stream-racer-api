import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { MapsResponse } from '../support/apiTypes';
import { pollUntil } from '../support/timing';

describe('GET /maps', () => {
  test('answers 202 while the list is loading and 200 with maps once fetched', async () => {
    const final = await pollUntil(
      () => get<MapsResponse>('/maps'),
      (response) => response.status === 200 && (response.json?.maps.length ?? 0) > 0,
      { attempts: 15, intervalMs: 1000 },
    );
    expect(final.status).toBe(200);
    const maps = final.json!;
    expect(maps.loading).toBe(false);
    expect(maps.maps.length).toBeGreaterThan(0);
  });

  test('every map has an id, a name and the official flag', async () => {
    const maps = (await get<MapsResponse>('/maps')).json!;
    for (const map of maps.maps) {
      expect(map.id).toEqual(expect.any(Number));
      expect(map.name).toEqual(expect.any(String));
      expect(map.official).toEqual(expect.any(Boolean));
    }
    expect(maps.maps.some((map) => map.official), 'no official map in the list').toBe(true);
  });
});
