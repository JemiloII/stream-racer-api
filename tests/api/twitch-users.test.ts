import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { ApiError, StreamerInfo, TwitchUsersResponse } from '../support/apiTypes';

// Resolution goes through Helix with the game's own Twitch token, so it needs the game logged in to Twitch.
// The streamer's own login (GET /me) is the one account guaranteed to exist.
describe('GET /twitch/users', () => {
  test('resolves a real login and drops one that does not exist', async (context) => {
    const streamer = (await get<StreamerInfo>('/me')).json!;
    if (!streamer.login) context.skip('game is not logged in to Twitch: nothing to resolve');
    const response = await get<TwitchUsersResponse | ApiError>(`/twitch/users?logins=${streamer.login},this_login_does_not_exist_xyz`);
    if (response.status === 409) context.skip(`game has no Twitch token: ${response.text}`);
    expect(response.status).toBe(200);
    const users = (response.json as TwitchUsersResponse).users;
    expect(users).toHaveLength(1);
    expect(users[0]!.login).toBe(streamer.login);
    expect(users[0]!.id).toBe(streamer.id);
    expect(users[0]!.displayName).toEqual(expect.any(String));
    expect(users[0]!.image).toMatch(/^https:\/\//);
  });

  test('refuses an empty login list with 400', async () => {
    const response = await get<ApiError>('/twitch/users?logins=');
    expect(response.status).toBe(400);
    expect(response.json!.error).toMatch(/logins/);
  });
});
