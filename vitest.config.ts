import { defineConfig } from 'vitest/config';
import { BaseSequencer, type TestSpecification } from 'vitest/node';

// The race flow is one story told across four files; they must run in this order. Everything else runs alphabetically.
const raceFileOrder = ['lobby', 'racing', 'camera', 'end'];

function raceRank(specification: TestSpecification): number {
  const fileName = specification.moduleId.replace(/\\/g, '/').split('/').pop() ?? '';
  const index = raceFileOrder.findIndex((stem) => fileName === `${stem}.test.ts`);
  return index === -1 ? raceFileOrder.length : index;
}

class StoryOrderSequencer extends BaseSequencer {
  override async sort(files: TestSpecification[]): Promise<TestSpecification[]> {
    return [...files].sort((left, right) => raceRank(left) - raceRank(right) || left.moduleId.localeCompare(right.moduleId));
  }
}

export default defineConfig({
  test: {
    // Globs are relative to --dir, so `pnpm run test:api` (--dir tests/api) and `test:race` (--dir tests/race) each pick
    // up their own folder; a bare `vitest run` takes both. tests/ui is Playwright's, tests/unit is xunit's.
    include: ['**/*.test.ts'],
    exclude: ['**/node_modules/**', 'tests/ui/**', 'tests/unit/**'],
    setupFiles: ['tests/support/requireGame.ts'],
    globalSetup: ['tests/race/raceGlobalSetup.ts'],
    // Every file talks to the same running game: never two files at once, never shuffled.
    fileParallelism: false,
    sequence: { sequencer: StoryOrderSequencer, shuffle: false, concurrent: false },
    testTimeout: 120_000, // a race test waits for lobbies to load and races to end
    hookTimeout: 60_000,
    reporters: ['default'],
  },
});
