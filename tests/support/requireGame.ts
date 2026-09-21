// Vitest setup file: every test file talks to the running game. When the API is unreachable, skip instead of failing.
import { beforeEach } from 'vitest';
import { gameProbe } from './gameOnline';

const probe = await gameProbe();
if (!probe.online) console.log(`[stream-racer-api] ${probe.reason}; skipping`);

beforeEach((context) => {
  if (!probe.online) context.skip(probe.reason);
});
