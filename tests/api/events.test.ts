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
