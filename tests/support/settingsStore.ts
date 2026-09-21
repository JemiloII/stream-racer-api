import { get, put } from './apiClient';
import type { SettingsDocument } from './apiTypes';

export async function readSettings(): Promise<SettingsDocument> {
  const response = await get<SettingsDocument>('/settings');
  if (!response.json) throw new Error(`GET /settings answered ${response.status}: ${response.text}`);
  return response.json;
}

/** PUT /settings merges by top-level key: send only what should change. Returns the full document as saved. */
export async function writeSettings(changes: Partial<SettingsDocument>): Promise<SettingsDocument> {
  const response = await put<SettingsDocument>('/settings', changes);
  if (!response.json) throw new Error(`PUT /settings answered ${response.status}: ${response.text}`);
  return response.json;
}
