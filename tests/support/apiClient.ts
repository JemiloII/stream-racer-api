// Thin fetch wrapper for the mod's HTTP API. Base URL from SR_API (default http://127.0.0.1:8793), bearer from SR_TOKEN.

export const apiBaseUrl = (process.env.SR_API ?? 'http://127.0.0.1:8793').replace(/\/$/, '');
export const apiToken = process.env.SR_TOKEN ?? '';

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE' | 'OPTIONS';

export interface ApiResponse<TBody = unknown> {
  status: number;
  ok: boolean;
  json: TBody | null;
  text: string;
  contentType: string;
}

export interface RequestOptions {
  method?: HttpMethod;
  body?: unknown;
  headers?: Record<string, string>;
  /** Replace (not merge) the default headers; the auth tests use it to send no bearer at all. */
  rawHeaders?: Record<string, string>;
}

export function authHeaders(): Record<string, string> {
  return apiToken ? { Authorization: `Bearer ${apiToken}` } : {};
}

export async function api<TBody = unknown>(path: string, options: RequestOptions = {}): Promise<ApiResponse<TBody>> {
  const method = options.method ?? (options.body === undefined ? 'GET' : 'POST');
  const headers = options.rawHeaders ?? { 'Content-Type': 'application/json', ...authHeaders(), ...options.headers };
  // A POST without a payload still needs an empty body (same reason curl needs -d '').
  const body = options.body === undefined ? (method === 'GET' ? undefined : '') : JSON.stringify(options.body);
  const response = await fetch(apiBaseUrl + path, { method, headers, body });
  const text = await response.text();
  let json: TBody | null = null;
  try {
    json = JSON.parse(text) as TBody;
  } catch {
    json = null;
  }
  return { status: response.status, ok: response.ok, json, text, contentType: response.headers.get('content-type') ?? '' };
}

type WithoutMethodAndBody = Omit<RequestOptions, 'method' | 'body'>;

export const get = <TBody = unknown>(path: string, options: WithoutMethodAndBody = {}) => api<TBody>(path, { ...options, method: 'GET' });

export const post = <TBody = unknown>(path: string, body?: unknown, options: WithoutMethodAndBody = {}) =>
  api<TBody>(path, body === undefined ? { ...options, method: 'POST' } : { ...options, method: 'POST', body });

export const put = <TBody = unknown>(path: string, body: unknown, options: WithoutMethodAndBody = {}) =>
  api<TBody>(path, { ...options, method: 'PUT', body });

/** Fetch a path the way a browser navigation does: Accept text/html, no JSON content type. */
export function fetchAsBrowser(path: string): Promise<ApiResponse<never>> {
  return api<never>(path, { method: 'GET', rawHeaders: { Accept: 'text/html,application/xhtml+xml', ...authHeaders() } });
}

/** URL of the SSE stream with the token as a query parameter (EventSource cannot set headers). */
export function eventsUrl(): string {
  return `${apiBaseUrl}/events${apiToken ? `?token=${encodeURIComponent(apiToken)}` : ''}`;
}
