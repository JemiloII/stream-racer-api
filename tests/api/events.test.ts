import { readFileSync, readdirSync } from 'node:fs';
import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';
import type { ScreenState } from '../support/apiTypes';
import { collectEvents, eventStreamContentType } from '../support/eventStream';

describe('GET /events', () => {
  test('is a server-sent event stream', async () => {
    expect(await eventStreamContentType()).toMatch(/text\/event-stream/);
  });

  test('is quiet outside a race: no pos or positions frames', async () => {
    const screen = (await get<ScreenState>('/screen')).json!;
    if (screen.running) return; // the 60 Hz stream is covered by tests/race/racing.test.ts
    const frames = await collectEvents({ durationMs: 1500, events: ['pos', 'positions'] });
    expect(frames).toEqual([]);
  });

  test('every event that does arrive carries JSON data', async () => {
    const events = await collectEvents({ durationMs: 1000 });
    for (const event of events) {
      expect(event.event).toEqual(expect.any(String));
      expect(typeof event.data).toBe('object');
    }
  });
});

// Every event the mod emits (Plugin.Emit in src/) must be in the catalogue on the API page and in the README, so the
// events viewer can subscribe to it and bots know it exists. Source files, not the served copy: the catalogue is what ships next.
describe('event catalogue', () => {
  const root = new URL('../../', import.meta.url);
  const source = (path: string) => readFileSync(new URL(path, root), 'utf8');
  // every C# file under src/ (Game/ and Camera/ are partial classes split by concern)
  const sources = readdirSync(new URL('src/', root), { recursive: true })
    .map(String)
    .filter((file) => file.endsWith('.cs'))
    .map((file) => 'src/' + file.replace(/\\/g, '/'));
  const emitted = () => new Set([...sources.map(source).join('\n').matchAll(/Plugin\.Emit\("([a-z_]+)"/g)].map((match) => match[1]!));
  const documented = () => {
    const block = source('ui/pages/api.js').match(/const EVENTS = \[([\s\S]*?)\n\];/)?.[1] ?? '';
    return new Set([...block.matchAll(/\["([a-z_]+)", "/g)].map((match) => match[1]!));
  };

  test('ui/pages/api.js lists every emitted event', () => {
    const missing = [...emitted()].filter((event) => !documented().has(event));
    expect(missing, 'events emitted in src/ but missing from the EVENTS list in ui/pages/api.js').toEqual([]);
  });

  test('boost pools have both events: boost (a pool spend, with boosts left) and boosts (a pool change)', () => {
    const catalogue = documented();
    for (const event of ['boost', 'boosts', 'perk', 'color', 'denied', 'chat']) expect(catalogue.has(event), event).toBe(true);
    const readme = source('README.md');
    for (const event of ['`boosts`', '`perk`', '`chat`']) expect(readme, `README events line mentions ${event}`).toContain(event);
  });
});
