import { describe, expect, test } from 'vitest';
import { put } from '../support/apiClient';
import type { ConfigResult } from '../support/apiTypes';
import { readSettings } from '../support/settingsStore';

// PUT /config is exercised only with values that are already set: a port or token change would restart the
// server under the test run (and the token would lock out every other client).
describe('PUT /config', () => {
  test('an unchanged value is accepted without a restart', async () => {
    const before = (await readSettings()).config;
    const response = await put<ConfigResult>('/config', { tickHz: before.tickHz, posHz: before.posHz, hotkeyBoost: before.hotkeyBoost });
    expect(response.status).toBe(200);
    const result = response.json!;
    expect(result.ok).toBe(true);
    expect(result.errors).toEqual([]);
    expect(result.restarting).toBe(false);
    expect(result.url).toMatch(/^http:\/\/127\.0\.0\.1:\d+\/$/);
    expect(result.config).toEqual(before);
  });

  test('an empty body changes nothing and echoes the config', async () => {
    const before = (await readSettings()).config;
    const response = await put<ConfigResult>('/config', {});
    expect(response.status).toBe(200);
    expect(response.json!.ok).toBe(true);
    expect(response.json!.config).toEqual(before);
  });

  test('an unknown hotkey name is reported, not applied', async () => {
    const before = (await readSettings()).config;
    const response = await put<ConfigResult>('/config', { hotkeyBoost: 'NotAKeyCode' });
    expect(response.status).toBe(200);
    expect(response.json!.ok).toBe(false);
    expect(response.json!.errors.join(' ')).toMatch(/unknown key/);
    expect(response.json!.config.hotkeyBoost).toBe(before.hotkeyBoost);
  });

  test('an out-of-range port is refused and the server keeps its port', async () => {
    const before = (await readSettings()).config;
    const response = await put<ConfigResult>('/config', { port: 80 });
    expect(response.status).toBe(200);
    expect(response.json!.ok).toBe(false);
    expect(response.json!.errors.join(' ')).toMatch(/port/);
    expect(response.json!.restarting).toBe(false);
    expect(response.json!.config.port).toBe(before.port);
  });
});
