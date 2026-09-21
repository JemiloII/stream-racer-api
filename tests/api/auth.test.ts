import { describe, expect, test } from 'vitest';
import { api, apiBaseUrl, apiToken, get } from '../support/apiClient';
import type { ApiError, VersionInfo } from '../support/apiTypes';
import { readSettings } from '../support/settingsStore';

// The token lives in the plugin config (PUT /config {token}). Changing it from a test would lock out every other
// client (control page, OBS docks, bots) for the duration of the run, so the token is never set here. What can be
// checked depends on how the game is configured:
//   - config.tokenRequired = false: anything goes, a stray bearer is ignored.
//   - config.tokenRequired = true and SR_TOKEN set: missing/wrong tokens are 401, the header and ?token= both work.
//   - tokenRequired but no SR_TOKEN: nothing runs (the setup file already skipped every suite with a 401 reason).
describe('API token', () => {
  test('GET /settings tells whether a token is required', async () => {
    const settings = await readSettings();
    expect(settings.config.tokenRequired).toEqual(expect.any(Boolean));
  });

  test('without a configured token the API is open and a bogus bearer is ignored', async (context) => {
    if ((await readSettings()).config.tokenRequired) context.skip('a token is configured: covered by the tests below');
    const anonymous = await api<VersionInfo>('/version', { method: 'GET', rawHeaders: {} });
    expect(anonymous.status).toBe(200);
    const wrongBearer = await api<VersionInfo>('/version', { method: 'GET', rawHeaders: { Authorization: 'Bearer definitely-not-it' } });
    expect(wrongBearer.status).toBe(200);
  });

  test('with a configured token a missing bearer is 401', async (context) => {
    if (!(await readSettings()).config.tokenRequired) context.skip('no token configured; nothing to lock');
    const anonymous = await api<ApiError>('/version', { method: 'GET', rawHeaders: {} });
    expect(anonymous.status).toBe(401);
    expect(anonymous.json!.error).toBe('unauthorized');
  });

  test('with a configured token a wrong bearer is 401', async (context) => {
    if (!(await readSettings()).config.tokenRequired) context.skip('no token configured; nothing to lock');
    const wrongBearer = await api<ApiError>('/version', { method: 'GET', rawHeaders: { Authorization: 'Bearer definitely-not-it' } });
    expect(wrongBearer.status).toBe(401);
  });

  test('with a configured token ?token= works for the event stream', async (context) => {
    if (!(await readSettings()).config.tokenRequired) context.skip('no token configured; nothing to lock');
    const controller = new AbortController();
    try {
      const withQuery = await fetch(`${apiBaseUrl}/events?token=${encodeURIComponent(apiToken)}`, { signal: controller.signal });
      expect(withQuery.status).toBe(200);
      expect(withQuery.headers.get('content-type')).toMatch(/text\/event-stream/);
    } finally {
      controller.abort();
    }
    const without = await fetch(`${apiBaseUrl}/events`);
    expect(without.status).toBe(401);
  });

  test('the control page itself loads without a token (the token is pasted on its Settings tab)', async () => {
    const shell = await api('/', { method: 'GET', rawHeaders: { Accept: 'text/html' } });
    expect(shell.status).toBe(200);
    expect(shell.contentType).toMatch(/text\/html/);
    expect((await get('/app.js', { rawHeaders: {} })).status).toBe(200);
  });
});
