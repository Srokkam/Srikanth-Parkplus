import { IST, formatInstant, nextDay, startOfDayUtc } from './time';

describe('time (stored in UTC, shown in IST by default)', () => {
  it('turns a calendar day in IST into the UTC instant it starts at', () => {
    expect(startOfDayUtc('2026-11-03', IST)).toBe('2026-11-02T18:30:00.000Z');
  });

  it('handles a zone with daylight saving on the day the clocks change', () => {
    // London moves from BST (UTC+1) to GMT on 25 Oct 2026; midnight that day is still BST.
    expect(startOfDayUtc('2026-10-25', 'Europe/London')).toBe('2026-10-24T23:00:00.000Z');
    expect(startOfDayUtc('2026-10-26', 'Europe/London')).toBe('2026-10-26T00:00:00.000Z');
  });

  it('moves to the next calendar day across month and year ends', () => {
    expect(nextDay('2026-11-30')).toBe('2026-12-01');
    expect(nextDay('2026-12-31')).toBe('2027-01-01');
  });

  it('shows a UTC instant in IST with the zone name', () => {
    const shown = formatInstant('2026-10-28T10:15:00.000Z', IST);
    expect(shown).toContain('28 Oct 2026');
    expect(shown).toMatch(/3:45\s?pm/i);
    expect(shown).toContain('IST');
  });
});
