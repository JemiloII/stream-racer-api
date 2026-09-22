import { beforeEach, describe, expect, test } from 'vitest';
import { get, post } from '../support/apiClient';
import type { ApiError, ChatStatus, SettingsDocument } from '../support/apiTypes';
import { installedFeatures, missingFeatureReason } from '../support/installedFeatures';

// Read-only on purpose: nothing here posts to the streamer's live chat. POST /chat/say is only checked for its 400.
describe('GET /chat', () => {
  beforeEach(async (context) => {
    if (!(await installedFeatures()).has('chatReplies')) context.skip(missingFeatureReason('chatReplies'));
  });

  test('reports the game chat connection, the reply switch and whether the token may send', async () => {
    const response = await get<ChatStatus>('/chat');
    expect(response.status, response.text).toBe(200);
    const chat = response.json!;
    expect(chat.connected).toEqual(expect.any(Boolean));
    expect(chat.replies).toEqual(expect.any(Boolean));
    expect(chat).toHaveProperty('channel');
    expect(chat).toHaveProperty('login');
    expect([null, true, false]).toContain(chat.canSend);
    if (chat.scopes !== null) expect(Array.isArray(chat.scopes)).toBe(true);
    if (chat.canSend === false) expect(chat.note).toMatch(/chat:edit/);
  });

  test('replies mirrors settings.chatReplies', async () => {
    const settings = (await get<SettingsDocument>('/settings')).json!;
    const chat = (await get<ChatStatus>('/chat')).json!;
    expect(chat.replies).toBe(settings.chatReplies);
  });
});

describe('POST /chat/say', () => {
  beforeEach(async (context) => {
    if (!(await installedFeatures()).has('chatReplies')) context.skip(missingFeatureReason('chatReplies'));
  });

  test('refuses an empty message with 400 (and so never reaches chat)', async () => {
    const response = await post<ApiError>('/chat/say');
    expect(response.status).toBe(400);
    expect(response.json!.error).toMatch(/text/);
    const blank = await post<ApiError>('/chat/say?text=%20%20');
    expect(blank.status).toBe(400);
  });

  test('anything else under /chat is 404', async () => {
    const response = await post<ApiError>('/chat/shout?text=hi');
    expect(response.status).toBe(404);
  });
});
