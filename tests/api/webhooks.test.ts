import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterAll, beforeAll, describe, expect, test } from 'vitest';
import type { SettingsDocument, Webhook } from '../support/apiTypes';
import { readSettings, writeSettings } from '../support/settingsStore';
import { sleep } from '../support/timing';

interface ReceivedRequest {
  method: string;
  path: string;
  contentType: string;
  headers: Record<string, string>;
  body: string;
}

// A throwaway HTTP server on a random high port stands in for "your bot". The `settings` event is the one event
// that fires outside a race (every PUT /settings and PUT /config emits it), so it drives the whole test.
describe('settings.webhooks', () => {
  let server: Server;
  let hookBase: string;
  const received: ReceivedRequest[] = [];
  let original: SettingsDocument;

  const receivedAt = (path: string) => received.filter((request) => request.path === path);

  /** The registering PUT fires the hook too (in the background), so deliveries are matched by content, not by count. */
  async function waitForDelivery(path: string, containing: string, timeoutMs = 5000): Promise<ReceivedRequest | undefined> {
    const deadline = Date.now() + timeoutMs;
    const matching = () => receivedAt(path).find((request) => request.body.includes(containing));
    while (!matching() && Date.now() < deadline) await sleep(100);
    return matching();
  }

  let marker = '';

  beforeAll(async () => {
    server = createServer((request, response) => {
      const chunks: Buffer[] = [];
      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        const headers: Record<string, string> = {};
        for (const [name, value] of Object.entries(request.headers)) if (typeof value === 'string') headers[name] = value;
        received.push({
          method: request.method ?? '',
          path: request.url ?? '',
          contentType: request.headers['content-type'] ?? '',
          headers,
          body: Buffer.concat(chunks).toString('utf8'),
        });
        response.statusCode = 204;
        response.end();
      });
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    hookBase = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
    original = await readSettings();
  });

  afterAll(async () => {
    await writeSettings({ webhooks: original.webhooks, ui: original.ui });
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  test('registering hooks keeps the existing list and answers with the merged list', async () => {
    const plainHook: Webhook = { event: 'settings', url: `${hookBase}/plain`, method: 'POST', header: 'X-Test-Hook: stream-racer', body: '', enabled: true };
    const templatedHook: Webhook = { event: 'settings', url: `${hookBase}/templated`, method: 'POST', header: '', body: '{"name":"{event}","payload":{json}}', enabled: true };
    const disabledHook: Webhook = { event: 'settings', url: `${hookBase}/disabled`, method: 'POST', header: '', body: '', enabled: false };
    const otherEventHook: Webhook = { event: 'race_end', url: `${hookBase}/race-end`, method: 'POST', header: '', body: '', enabled: true };
    const saved = await writeSettings({ webhooks: [...original.webhooks, plainHook, templatedHook, disabledHook, otherEventHook] });
    expect(saved.webhooks).toHaveLength(original.webhooks.length + 4);
    expect(saved.webhooks).toEqual(expect.arrayContaining([plainHook, templatedHook, disabledHook, otherEventHook]));
  });

  test('a PUT /settings fires the settings hook with the full settings document as JSON', async () => {
    marker = `webhook-${Date.now()}`;
    await writeSettings({ ui: { ...original.ui, webhookMarker: marker } });
    const delivery = await waitForDelivery('/plain', marker);
    expect(delivery, 'webhook never delivered the marker').toBeDefined();
    expect(delivery!.method).toBe('POST');
    expect(delivery!.contentType).toMatch(/application\/json/);
    expect(delivery!.headers['x-test-hook']).toBe('stream-racer');
    const payload = JSON.parse(delivery!.body) as SettingsDocument;
    expect(payload.ui['webhookMarker']).toBe(marker);
    expect(payload.config.port).toBe(original.config.port);
    expect(payload).not.toHaveProperty('twitchToken');
  });

  test('a templated body gets {event} and {json} substituted', async () => {
    const delivery = await waitForDelivery('/templated', marker);
    expect(delivery).toBeDefined();
    const payload = JSON.parse(delivery!.body) as { name: string; payload: SettingsDocument };
    expect(payload.name).toBe('settings');
    expect(payload.payload.ui['webhookMarker']).toBe(marker);
    expect(payload.payload.config.port).toBe(original.config.port);
  });

  test('disabled hooks and hooks for other events stay silent', async () => {
    await sleep(500);
    expect(receivedAt('/disabled')).toEqual([]);
    expect(receivedAt('/race-end')).toEqual([]);
  });

  test('removing the hooks stops the calls', async () => {
    await writeSettings({ webhooks: original.webhooks });
    await writeSettings({ ui: { ...original.ui, webhookMarker: 'after-removal' } });
    await sleep(750);
    expect(await waitForDelivery('/plain', 'after-removal', 0)).toBeUndefined();
    expect(await waitForDelivery('/templated', 'after-removal', 0)).toBeUndefined();
    expect((await readSettings()).webhooks).toEqual(original.webhooks);
  });
});
