import { Injectable, signal } from '@angular/core';

/** Times are stored in UTC and shown in the shopper's zone: India Standard Time unless they choose their device's zone. */
export const IST = 'Asia/Kolkata';

const STORAGE_KEY = 'ipl.timeZone';

export function deviceTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || IST;
}

/** "2026-10-28T10:15:00Z" in IST -> "28 Oct 2026, 3:45 pm IST". */
export function formatInstant(utc: string, timeZone: string): string {
  const instant = new Date(utc);
  const dateTime = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(instant);
  const zone = new Intl.DateTimeFormat('en-IN', { timeZone, timeZoneName: 'short' })
    .formatToParts(instant)
    .find((part) => part.type === 'timeZoneName')?.value;
  return zone ? `${dateTime} ${zone}` : dateTime;
}

/** Midnight at the start of a calendar day ("2026-11-03") in a zone, as a UTC ISO string. */
export function startOfDayUtc(day: string, timeZone: string): string {
  const [year, month, date] = day.split('-').map(Number);
  const wallClock = Date.UTC(year, month - 1, date);
  // Two passes, so a daylight-saving change on that day still lands on the right instant.
  const first = wallClock - offsetMs(new Date(wallClock), timeZone);
  return new Date(wallClock - offsetMs(new Date(first), timeZone)).toISOString();
}

/** "2026-11-30" -> "2026-12-01". */
export function nextDay(day: string): string {
  const [year, month, date] = day.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, date + 1)).toISOString().slice(0, 10);
}

/** How far a zone's wall clock is ahead of UTC at an instant. */
function offsetMs(instant: Date, timeZone: string): number {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
      .formatToParts(instant)
      .map((part) => [part.type, part.value]),
  );
  const asUtc = Date.UTC(+parts['year'], +parts['month'] - 1, +parts['day'], +parts['hour'], +parts['minute'], +parts['second']);
  return asUtc - Math.floor(instant.getTime() / 1000) * 1000;
}

/** The zone times are shown in. Remembered on this device; IST until the shopper picks another. */
@Injectable({ providedIn: 'root' })
export class TimeZoneSetting {
  readonly options: ReadonlyArray<{ value: string; label: string }> = [
    { value: IST, label: 'India Standard Time (IST)' },
    ...(deviceTimeZone() === IST ? [] : [{ value: deviceTimeZone(), label: `This device (${deviceTimeZone()})` }]),
  ];

  readonly zone = signal(this.read());

  set(zone: string): void {
    const known = this.options.some((o) => o.value === zone) ? zone : IST;
    this.zone.set(known);
    try {
      localStorage.setItem(STORAGE_KEY, known);
    } catch {
      // Storage blocked: the choice lasts for this page only.
    }
  }

  private read(): string {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      return saved && this.options.some((o) => o.value === saved) ? saved : IST;
    } catch {
      return IST;
    }
  }
}
