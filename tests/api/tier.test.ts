// PUT /tier/:login — an outside bot tells the mod a viewer's follower/subscriber status. Restored afterwards.
import { afterAll, describe, expect, test } from 'vitest';
import { api, get, post } from '../support/apiClient';
import type { ApiError } from '../support/apiTypes';

const login = 'sr_tier_test_viewer';
interface TierResult { login: string; follower: boolean | null; subscriber: boolean | null; source: string; inRace: boolean; granted?: number }

describe('PUT /tier/:login', () => {
  afterAll(async () => { await api('/tier', { method: 'DELETE' }); });

  test('stores follower and subscriber flags and reads them back', async () => {
    const stored = await api<TierResult>(`/tier/${login}`, { method: 'PUT', body: { follower: true, source: 'test' } });
    expect(stored.status, stored.text).toBe(200);
    expect(stored.json).toMatchObject({ login, follower: true, subscriber: null, source: 'test', inRace: false });
    const updated = await api<TierResult>(`/tier/${login}`, { method: 'PUT', body: { subscriber: true } });
    expect(updated.json).toMatchObject({ follower: true, subscriber: true });
    const readBack = await get<TierResult>(`/tier/${login.toUpperCase()}`);
    expect(readBack.json).toMatchObject({ login, follower: true, subscriber: true });
  });

  test('refuses an empty body and 404s an unknown login', async () => {
    expect((await api<ApiError>(`/tier/${login}`, { method: 'PUT', body: {} })).status).toBe(400);
    expect((await get<ApiError>('/tier/nobody_pushed_this')).status).toBe(404);
    expect((await post<ApiError>('/tier')).status).toBe(400);
  });

  test('once a bot pushes tiers, follower checks report "bot"', async () => {
    await api(`/tier/${login}`, { method: 'PUT', body: { follower: false } });
    const perks = (await get<{ followerChecks: string }>(`/perks/${login}`)).json!;
    expect(perks.followerChecks).toBe('bot');
  });
});
