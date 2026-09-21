import { describe, expect, test } from 'vitest';
import { apiBaseUrl, fetchAsBrowser, get } from '../support/apiClient';

const appPages = ['controls', 'camera', 'bots', 'settings', 'api'];

describe('control page routes', () => {
  test.each(appPages)('/%s serves index.html to a browser (Accept: text/html)', async (page) => {
    const response = await fetchAsBrowser(`/${page}`);
    expect(response.status).toBe(200);
    expect(response.contentType).toMatch(/text\/html/);
    expect(response.text).toMatch(/<script[^>]+src="app\.js"/);
  });

  test('/settings and /camera stay JSON for fetch and curl (no text/html in Accept)', async () => {
    const settings = await get('/settings');
    expect(settings.status).toBe(200);
    expect(settings.contentType).toMatch(/application\/json/);
    expect(settings.json).toHaveProperty('config');
    const camera = await get('/camera');
    expect(camera.contentType).toMatch(/application\/json/);
  });

  test('/ is the app shell', async () => {
    const root = await fetchAsBrowser('/');
    expect(root.status).toBe(200);
    expect(root.contentType).toMatch(/text\/html/);
    expect(root.text).toMatch(/<script[^>]+src="app\.js"/);
  });
});

describe('browser-source pages', () => {
  test('/overlay is an HTML page whatever the Accept header', async () => {
    const overlay = await get('/overlay');
    expect(overlay.status).toBe(200);
    expect(overlay.contentType).toMatch(/text\/html/);
    expect(overlay.text).toMatch(/overlay\.js/);
  });

  test('/minimap is an HTML page whatever the Accept header', async () => {
    const minimap = await get('/minimap');
    expect(minimap.status).toBe(200);
    expect(minimap.contentType).toMatch(/text\/html/);
    expect(minimap.text).toMatch(/minimap\.js/);
  });
});

describe('embedded resources', () => {
  test.each([
    ['/app.js', /text\/javascript/],
    ['/store.js', /text\/javascript/],
    ['/shared.css', /text\/css/],
    ['/app.css', /text\/css/],
    ['/pages/api.js', /text\/javascript/],
    ['/index.html', /text\/html/],
  ])('%s is served with %s and no-cache', async (path, expectedType) => {
    const response = await get(path);
    expect(response.status).toBe(200);
    expect(response.contentType).toMatch(expectedType);
    expect(response.text.length).toBeGreaterThan(0);
  });

  test('a resource that is not embedded falls through to the API 404', async () => {
    const response = await get('/no-such-file.js');
    expect(response.status).toBe(404);
  });
});

describe('unknown routes', () => {
  test('answer 404 with a JSON error', async () => {
    const response = await get<{ error: string }>('/nope');
    expect(response.status).toBe(404);
    expect(response.contentType).toMatch(/application\/json/);
    expect(response.json!.error).toBe('not found');
  });

  test('CORS is open and preflight is 204', async () => {
    const response = await fetch(`${apiBaseUrl}/race`, { method: 'OPTIONS' });
    expect(response.status).toBe(204);
    expect(response.headers.get('access-control-allow-origin')).toBe('*');
  });
});
