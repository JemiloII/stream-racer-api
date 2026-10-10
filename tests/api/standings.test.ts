import { describe, expect, test } from 'vitest';
import { get } from '../support/apiClient';

// GET /standings: totals from the races saved on this PC. Read-only (the file only grows at race_end).
interface StandingsResponse {
  period: string;
  since: string | null;
  races: number;
  standings: {
    rank: number;
    login: string;
    points: number;
    wins: number;
    races: number;
  }[];
}

describe('GET /standings', () => {
  test.each(['day', 'week', 'month', 'all'])('period=%s answers with that period', async (period) => {
    const response = await get<StandingsResponse>(`/standings?period=${period}`);
    expect(response.status).toBe(200);
    expect(response.json!.period).toBe(period);
    expect(Array.isArray(response.json!.standings)).toBe(true);
    if (period === 'all') {
      expect(response.json!.since).toBeNull();
    } else {
      expect(response.json!.since).toEqual(expect.any(String));
    }
  });

  test('an unknown period falls back to the week', async () => {
    expect((await get<StandingsResponse>('/standings?period=fortnight')).json!.period).toBe('week');
  });

  test('limit caps the rows and ranks start at 1, best first', async () => {
    const standings = (await get<StandingsResponse>('/standings?period=all&limit=3')).json!.standings;
    expect(standings.length).toBeLessThanOrEqual(3);
    standings.forEach((standing, index) => expect(standing.rank).toBe(index + 1));
    const points = standings.map((standing) => standing.points);
    expect(points).toEqual([...points].sort((left, right) => right - left));
  });
});
