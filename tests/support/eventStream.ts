// Read the /events SSE stream with fetch (Node has no EventSource that takes headers).
import { eventsUrl } from './apiClient';

export interface SseEvent<TData = unknown> {
  event: string;
  data: TData;
}

export interface CollectOptions {
  /** How long to listen. */
  durationMs: number;
  /** Keep only these event names (all when omitted). */
  events?: string[];
  /** Stop early once an event passes this check. */
  stopWhen?: (event: SseEvent) => boolean;
}

/** Open the stream and gather events for `durationMs` (or until `stopWhen` says so). Data is JSON-parsed. */
export async function collectEvents<TData = unknown>(options: CollectOptions): Promise<SseEvent<TData>[]> {
  const controller = new AbortController();
  const collected: SseEvent<TData>[] = [];
  const timer = setTimeout(() => controller.abort(), options.durationMs);
  try {
    const response = await fetch(eventsUrl(), { signal: controller.signal, headers: { Accept: 'text/event-stream' } });
    if (!response.ok || !response.body) throw new Error(`GET /events answered ${response.status}`);
    let buffer = '';
    let eventName = 'message';
    let dataLines: string[] = [];
    for await (const chunk of response.body.pipeThrough(new TextDecoderStream())) {
      buffer += chunk;
      let newline = buffer.indexOf('\n');
      while (newline >= 0) {
        const line = buffer.slice(0, newline).replace(/\r$/, '');
        buffer = buffer.slice(newline + 1);
        newline = buffer.indexOf('\n');
        if (line.startsWith('event:')) eventName = line.slice(6).trim();
        else if (line.startsWith('data:')) dataLines.push(line.slice(5).trim());
        else if (line === '') {
          if (dataLines.length > 0) {
            const parsed: SseEvent<TData> = { event: eventName, data: JSON.parse(dataLines.join('\n')) as TData };
            if (!options.events || options.events.includes(eventName)) {
              collected.push(parsed);
              if (options.stopWhen?.(parsed)) controller.abort();
            }
          }
          eventName = 'message';
          dataLines = [];
        }
      }
    }
  } catch (error) {
    if (!(error instanceof Error && error.name === 'AbortError')) throw error;
  } finally {
    clearTimeout(timer);
  }
  return collected;
}

/** Resolve with the first `eventName` event within `timeoutMs`, or null. Start it before the action that fires the event. */
export async function waitForEvent<TData = unknown>(eventName: string, timeoutMs: number): Promise<SseEvent<TData> | null> {
  const events = await collectEvents<TData>({ durationMs: timeoutMs, events: [eventName], stopWhen: () => true });
  return events[0] ?? null;
}

/** Content type of the stream endpoint; the connection is dropped right away. */
export async function eventStreamContentType(): Promise<string> {
  const controller = new AbortController();
  try {
    const response = await fetch(eventsUrl(), { signal: controller.signal });
    return response.headers.get('content-type') ?? '';
  } finally {
    controller.abort();
  }
}
