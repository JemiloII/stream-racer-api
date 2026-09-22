import type { Page } from "@playwright/test";

// Replaces the page's EventSource with a stand-in the spec drives: `emitEvent(page, "race_end", snapshot)` delivers
// a named SSE event to every open stream exactly like the game would. The live /events stream is never opened, so
// what the spec sees is only what it sent (the 4 Hz snapshots from a real race can't interfere).
export async function installFakeEvents(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const sources: FakeEventSource[] = [];
    class FakeEventSource extends EventTarget {
      static readonly CONNECTING = 0; static readonly OPEN = 1; static readonly CLOSED = 2;
      readonly url: string;
      readyState = 0;
      onopen: ((event: Event) => void) | null = null;
      onerror: ((event: Event) => void) | null = null;
      onmessage: ((event: MessageEvent) => void) | null = null;
      constructor(url: string) {
        super();
        this.url = url;
        sources.push(this);
        setTimeout(() => { if (this.readyState === 2) return; this.readyState = 1; const open = new Event("open"); this.onopen?.(open); this.dispatchEvent(open); }, 0);
      }
      close(): void { this.readyState = 2; const index = sources.indexOf(this); if (index >= 0) sources.splice(index, 1); }
    }
    (window as unknown as { EventSource: unknown }).EventSource = FakeEventSource;
    (window as unknown as { __fakeEvents: unknown }).__fakeEvents = {
      emit(name: string, data: unknown) { for (const source of [...sources]) source.dispatchEvent(new MessageEvent(name, { data: JSON.stringify(data) })); return sources.length; },
      openStreams() { return sources.map((source) => source.url); },
    };
  });
}

/** Deliver one SSE event to every stream the page opened. Resolves to how many streams got it. */
export async function emitEvent(page: Page, name: string, data: unknown): Promise<number> {
  return page.evaluate(([eventName, payload]) => (window as unknown as { __fakeEvents: { emit(n: string, d: unknown): number } }).__fakeEvents.emit(eventName as string, payload), [name, data]);
}

/** URLs of the event streams the page has open (the overlays open exactly one). */
export async function openStreams(page: Page): Promise<string[]> {
  return page.evaluate(() => (window as unknown as { __fakeEvents: { openStreams(): string[] } }).__fakeEvents.openStreams());
}

/** Answer GET `path` with this JSON instead of asking the game (registered last, so it wins over the working-tree router). */
export async function mockJson(page: Page, path: string, body: unknown): Promise<void> {
  await page.route((url) => url.pathname === path, (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) }));
}
