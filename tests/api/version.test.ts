import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { VersionInfo } from '../support/apiTypes';
import { sourceVersion } from '../support/sourceVersion';

describe('GET /version', () => {
  test('reports the mod, game, Unity and BepInEx versions plus the update check', async () => {
    const response = await get<VersionInfo>('/version');
    expect(response.status).toBe(200);
    expect(response.contentType).toMatch(/application\/json/);
    const version = response.json!;
    expect(version.api).toMatch(/^\d+\.\d+\.\d+$/);
    expect(version.commit).toEqual(expect.any(String));
    expect(version.game).toEqual(expect.any(String));
    expect(version.unity).toMatch(/^\d{4}\./);
    expect(version.bepinex).toMatch(/^\d+\.\d+\.\d+$/);
    expect(version).toHaveProperty('latest');
    expect(version).toHaveProperty('upToDate');
    expect(version).toHaveProperty('updateUrl');
    expect(version.developer).toBe('Shibiko');
    expect(version.twitch).toMatch(/^https:\/\/twitch\.tv\//);
  });

  test('the running DLL is the build from src/Plugin.cs', async () => {
    const version = (await get<VersionInfo>('/version')).json!;
    expect(version.api, 'installed DLL is stale: rebuild with ./install.sh (game closed)').toBe(sourceVersion());
  });
});
