import { readFileSync } from 'node:fs';

/** The `Version = "x.y.z"` constant in src/Plugin.cs. The running DLL must report the same, or you are testing a stale build. */
export function sourceVersion(): string {
  const pluginSource = readFileSync(new URL('../../src/Plugin.cs', import.meta.url), 'utf8');
  const match = pluginSource.match(/Version = "(\d+\.\d+\.\d+)"/);
  if (!match?.[1]) throw new Error('no Version = "x.y.z" constant in src/Plugin.cs');
  return match[1];
}
