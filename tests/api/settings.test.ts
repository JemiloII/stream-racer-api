import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import { get, put } from '../support/apiClient';
import type { SettingsDocument } from '../support/apiTypes';
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
