import { afterAll, beforeAll, beforeEach, describe, expect, test } from 'vitest';
import { get, put } from '../support/apiClient';
import { shotKeys, type SettingsDocument } from '../support/apiTypes';
import { installedFeatures, missingFeatureReason } from '../support/installedFeatures';
import { readSettings, writeSettings } from '../support/settingsStore';

const requiredSections: (keyof SettingsDocument)[] = [
  'autoJoinStreamer', 'streamerColor', 'autoJoin', 'customBots', 'botOptions', 'webhooks', 'bots',
  'colorCommand', 'colorCommandEnabled', 'respawnCommand', 'respawnCommandEnabled', 'colors', 'perks',
  'ui', 'overlay', 'minimap', 'twitchClientId', 'twitchTokenSet', 'config',
];

describe('GET /settings', () => {
  test('has every section the pages rely on', async () => {
    const response = await get<SettingsDocument>('/settings');
    expect(response.status).toBe(200);
    expect(response.contentType).toMatch(/application\/json/);
    const settings = response.json!;
    for (const section of requiredSections) expect(settings, `settings.${section}`).toHaveProperty(section);
    expect(Array.isArray(settings.autoJoin)).toBe(true);
    expect(Array.isArray(settings.webhooks)).toBe(true);
    expect(settings.bots.length).toBeGreaterThan(0);
    expect(settings.minimap.enabled).toEqual(expect.any(Boolean));
    expect(settings.minimap.aspect).toMatch(/^\d+:\d+$/);
    expect(settings.config.port).toBeGreaterThan(0);
    expect(settings.config.tokenRequired).toEqual(expect.any(Boolean));
  });

  test('never leaks the Twitch token, only whether one is set', async () => {
    const settings = (await get<SettingsDocument>('/settings')).json!;
    expect(settings).not.toHaveProperty('twitchToken');
    expect(settings.twitchTokenSet).toEqual(expect.any(Boolean));
  });
});

describe('PUT /settings', () => {
  let original: SettingsDocument;

  beforeAll(async () => {
    original = await readSettings();
  });

  afterAll(async () => {
    await writeSettings({ ui: original.ui });
  });

  test('merges: touching one section keeps every other section', async () => {
    const marker = `api-test-${Date.now()}`;
    const merged = await writeSettings({ ui: { ...original.ui, testMarker: marker } });
    expect(merged.ui['testMarker']).toBe(marker);
    expect(merged.bots).toEqual(original.bots);
    expect(merged.autoJoin).toEqual(original.autoJoin);
    expect(merged.customBots).toEqual(original.customBots);
    expect(merged.webhooks).toEqual(original.webhooks);
    expect(merged.minimap).toEqual(original.minimap);
    expect(merged.perks).toEqual(original.perks);
    expect(merged.colorCommand).toBe(original.colorCommand);
  });

  test('a section sent whole replaces that section (restoring ui drops the marker)', async () => {
    const restored = await writeSettings({ ui: original.ui });
    expect(restored.ui).not.toHaveProperty('testMarker');
    expect(restored.ui).toEqual(original.ui);
  });

  test('config is read-only through /settings (goes through PUT /config instead)', async () => {
    const attempt = await put<SettingsDocument>('/settings', { config: { ...original.config, port: 1 } });
    expect(attempt.status).toBe(200);
    expect(attempt.json!.config).toEqual(original.config);
    expect((await readSettings()).config).toEqual(original.config);
  });
});

describe('newer sections: chat commands, chat replies, follower checks, camera shots', () => {
  let original: SettingsDocument;

  beforeEach(async (context) => {
    const features = await installedFeatures();
    for (const feature of ['camera', 'chatReplies', 'followerChecks'] as const) if (!features.has(feature)) context.skip(missingFeatureReason(feature));
    original ??= await readSettings();
  });

  afterAll(async () => {
    if (original) await writeSettings({ camera: original.camera, respawnCommand: original.respawnCommand });
  });

  test('chat commands are alias lists ("a|b"), chatReplies a switch, followerChecks a status', async () => {
    const settings = await readSettings();
    expect(settings.respawnCommand).toEqual(expect.any(String));
    expect(settings.colorCommand).toEqual(expect.any(String));
    expect(settings.chatReplies).toEqual(expect.any(Boolean));
    expect(['ok', 'no token', 'unknown']).toContain(settings.followerChecks);
    if (!settings.twitchTokenSet) expect(settings.followerChecks).toBe('no token');
    expect(settings).toHaveProperty('followerChecksError');
  });

  test('camera.shots has every director shot, all booleans', async () => {
    const settings = await readSettings();
    for (const key of shotKeys) expect(settings.camera.shots[key], `camera.shots.${key}`).toEqual(expect.any(Boolean));
  });

  test('a partial camera.shots PUT keeps the unmentioned shots on (never off by accident)', async () => {
    const flipped = !original.camera.shots.sweep;
    const merged = await writeSettings({ camera: { shots: { sweep: flipped } as SettingsDocument['camera']['shots'] } });
    expect(merged.camera.shots.sweep).toBe(flipped);
    for (const key of shotKeys) if (key !== 'sweep') expect(merged.camera.shots[key], `camera.shots.${key}`).toBe(true);
    const restored = await writeSettings({ camera: original.camera });
    expect(restored.camera.shots).toEqual(original.camera.shots);
  });

  test('an empty respawn command falls back to the default aliases', async () => {
    const cleared = await writeSettings({ respawnCommand: '' });
    expect(cleared.respawnCommand).toBe('!race respawn|!respawn');
    const restored = await writeSettings({ respawnCommand: original.respawnCommand });
    expect(restored.respawnCommand).toBe(original.respawnCommand);
  });
});
