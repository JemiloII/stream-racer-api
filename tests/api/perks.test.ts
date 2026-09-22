import { beforeEach, describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { ApiError, PerksInfo, SettingsDocument } from '../support/apiTypes';
import { installedFeatures, missingFeatureReason } from '../support/installedFeatures';

const followerStatuses = ['ok', 'no token', 'unknown', 'bot'];

// A login that is not in any race and not on Twitch: the answer is about the settings, not the person.
const nobody = 'sr_api_test_nobody';

describe('GET /perks/:login', () => {
  beforeEach(async (context) => {
    if (!(await installedFeatures()).has('followerChecks')) context.skip(missingFeatureReason('followerChecks'));
  });

  test('explains what a viewer gets and why', async () => {
    const response = await get<PerksInfo>(`/perks/${nobody.toUpperCase()}`);
    expect(response.status, response.text).toBe(200);
    const perks = response.json!;
    expect(perks.login).toBe(nobody); // lowercased like every other login
    expect(perks.inRace).toBe(false);
    expect(perks.follower).toBe(false);
    expect(perks.subscriber).toBe(false);
    expect(perks.developer).toBe(false);
    expect(perks.host).toBe(false);
    expect(perks.extraBoosts).toEqual(expect.any(Number));
    expect(Array.isArray(perks.why)).toBe(true);
    for (const reason of perks.why) expect(reason).toEqual(expect.any(String));
    expect(followerStatuses).toContain(perks.followerChecks);
    expect(perks.granted).toBe(0);
    expect(perks.boosts).toBeNull();
    expect(perks.perks).toHaveProperty('boostFollower');
  });

  test('followerChecks matches the settings: no token pasted = "no token", and the reason says so', async () => {
    const settings = (await get<SettingsDocument>('/settings')).json!;
    const perks = (await get<PerksInfo>(`/perks/${nobody}`)).json!;
    expect(perks.followerChecks).toBe(settings.followerChecks);
    if ((!settings.twitchTokenSet || !settings.twitchClientId) && settings.followerChecks !== 'bot') {
      expect(perks.followerChecks).toBe('no token');
      if (settings.perks.boostFollower !== 0) expect(perks.why.join(' ')).toMatch(/token/);
    }
  });

  test('a nobody with only the follower perk configured gets nothing (sub / dev / host are false)', async () => {
    const perks = (await get<PerksInfo>(`/perks/${nobody}`)).json!;
    if (perks.follower) return; // a token is set and, somehow, this login follows: not the case under test
    expect(perks.extraBoosts).toBe(0);
  });

  test('needs a login', async () => {
    const response = await get<ApiError>('/perks');
    expect(response.status).toBe(400);
  });
});
