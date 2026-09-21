import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { RaceSnapshot, ScreenState, StreamerInfo } from '../support/apiTypes';

describe('GET /race', () => {
  test('is a snapshot: running flag, lobby flag, streamer, map and a vehicle list', async () => {
    const response = await get<RaceSnapshot>('/race');
    expect(response.status).toBe(200);
    const snapshot = response.json!;
    expect(snapshot.running).toEqual(expect.any(Boolean));
    expect(snapshot.lobby).toEqual(expect.any(Boolean));
    expect(snapshot).toHaveProperty('streamer');
    expect(snapshot).toHaveProperty('map');
    expect(Array.isArray(snapshot.vehicles)).toBe(true);
  });

  test('agrees with /screen about the race state and car count', async () => {
    const snapshot = (await get<RaceSnapshot>('/race')).json!;
    const screen = (await get<ScreenState>('/screen')).json!;
    expect(snapshot.running).toBe(screen.running);
    expect(snapshot.lobby).toBe(screen.lobby);
    expect(snapshot.vehicles.length).toBe(screen.vehicles);
  });

  test('every vehicle carries the documented fields', async () => {
    const snapshot = (await get<RaceSnapshot>('/race')).json!;
    for (const vehicle of snapshot.vehicles) {
      expect(vehicle.login).toEqual(expect.any(String));
      expect(vehicle.place).toBeGreaterThanOrEqual(1);
      expect(vehicle.color).toMatch(/^#[0-9a-f]{6}$/i);
      expect(vehicle.finished).toEqual(expect.any(Boolean));
      expect(vehicle.boosts).toBeGreaterThanOrEqual(0);
      expect(vehicle.pct).toBeGreaterThanOrEqual(0);
      expect(vehicle.pct).toBeLessThanOrEqual(100);
      expect(vehicle.state).toEqual(expect.any(String));
      expect(vehicle.x).toEqual(expect.any(Number));
      expect(vehicle.z).toEqual(expect.any(Number));
    }
  });
});

describe('GET /me', () => {
  test('names the logged-in streamer and whether their car is in the race', async () => {
    const response = await get<StreamerInfo>('/me');
    expect(response.status).toBe(200);
    const streamer = response.json!;
    expect(streamer).toHaveProperty('id');
    expect(streamer).toHaveProperty('login');
    expect(streamer.inRace).toEqual(expect.any(Boolean));
    const snapshot = (await get<RaceSnapshot>('/race')).json!;
    expect(snapshot.streamer).toBe(streamer.login);
  });
});
