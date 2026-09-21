export const sleep = (milliseconds: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, milliseconds));

export interface PollOptions {
  attempts: number;
  intervalMs: number;
}

/** Call `probe` until `accept` passes or the attempts run out; returns the last value either way. */
export async function pollUntil<TValue>(probe: () => Promise<TValue>, accept: (value: TValue) => boolean, options: PollOptions): Promise<TValue> {
  let latest = await probe();
  for (let attempt = 1; attempt < options.attempts && !accept(latest); attempt++) {
    await sleep(options.intervalMs);
    latest = await probe();
  }
  return latest;
}
