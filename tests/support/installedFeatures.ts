// Which of the newer settings sections the running DLL exposes. A test about a route or section that the installed build
// predates skips with a printed reason (rebuild + ./install.sh with the game closed) instead of failing on a 404.
import { get } from './apiClient';

const featureKeys = ['camera', 'chatReplies', 'followerChecks'] as const;
export type Feature = (typeof featureKeys)[number];

let cached: Promise<Set<Feature>> | undefined;

async function probe(): Promise<Set<Feature>> {
  const found = new Set<Feature>();
  try {
    const settings = (await get<Record<string, unknown>>('/settings')).json;
    if (settings) for (const key of featureKeys) if (key in settings) found.add(key);
  } catch {
    // game down: requireGame.ts skips everything anyway
  }
  return found;
}

/** One probe per worker. */
export function installedFeatures(): Promise<Set<Feature>> {
  cached ??= probe();
  return cached;
}

export function missingFeatureReason(feature: Feature): string {
  return `the installed DLL predates settings.${feature}: dotnet build + ./install.sh with the game closed, then rerun`;
}
