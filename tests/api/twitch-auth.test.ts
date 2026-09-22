// /twitch/auth, /twitch/callback, /twitch/token — the mod's own Twitch login. Read-only: never stores or forgets a token.
import { describe, expect, test } from 'vitest';
import { apiBaseUrl, get, post } from '../support/apiClient';
import type { ApiError } from '../support/apiTypes';

interface TwitchStatus { connected: boolean; login: string; scopes: string[]; wanted: string[]; redirectUri: string; features: { followerChecks: boolean; chatReplies: boolean }; missing: string[] }

describe('Twitch login', () => {
  test('GET /twitch/token reports the status shape', async () => {
    const status = (await get<TwitchStatus>('/twitch/token')).json!;
    expect(typeof status.connected).toBe('boolean');
    expect(status.wanted).toContain('moderator:read:followers');
    expect(status.wanted).toContain('user:write:chat');
    expect(status.redirectUri).toMatch(/^http:\/\/localhost:\d+\/twitch\/callback$/);
    expect(status.features.followerChecks).toBe(status.scopes.includes('moderator:read:followers') && status.connected);
    expect(status.features.chatReplies).toBe(status.scopes.includes('user:write:chat') && status.connected);
  });

  test('GET /twitch/callback is the token-catching page', async () => {
    const response = await fetch(`${apiBaseUrl}/twitch/callback`);
    expect(response.headers.get('content-type')).toMatch(/text\/html/);
    expect(await response.text()).toContain('access_token');
  });

  test('POST /twitch/token without a token is 400; a made-up token is rejected', async () => {
    expect((await post<ApiError>('/twitch/token', {})).status).toBe(400);
    const rejected = await post<ApiError>('/twitch/token', { token: 'not-a-real-token' });
    expect(rejected.status).toBe(401);
    expect(rejected.json!.error).toMatch(/rejected/);
  });

  test('GET /twitch/auth redirects to Twitch when a client id is set, else 400', async () => {
    const status = (await get<TwitchStatus & { clientId: string }>('/twitch/token')).json!;
    const response = await fetch(`${apiBaseUrl}/twitch/auth`, { redirect: 'manual' });
    if (status.clientId) {
      expect(response.status).toBe(302);
      expect(response.headers.get('location')).toMatch(/^https:\/\/id\.twitch\.tv\/oauth2\/authorize\?response_type=token/);
    } else {
      expect(response.status).toBe(400);
    }
  });
});
