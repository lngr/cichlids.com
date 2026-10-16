/** A local wall clock date ("YYYY-MM-DD") and time ("HH:mm") as a member types them. */
export type LocalDateTime = { date: string; time: string };

/** When a care entry happened: right now, a relative day, a chosen local date and time, or (for an existing entry) unchanged. */
export type WhenChoice =
  | { kind: "now" | "keep" | "yesterday" | "twoDaysAgo" }
  | ({ kind: "custom" } & LocalDateTime);

export type WhenError = "invalid" | "future";
export type ResolvedWhen = { occurredAt: string | null; error: WhenError | null };

const DAY_MS = 24 * 60 * 60 * 1000;
const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(timeZone: string): Intl.DateTimeFormat {
  let found = formatters.get(timeZone);
  if (!found) {
    found = new Intl.DateTimeFormat("en-US", {
      timeZone, hourCycle: "h23", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit",
    });
    formatters.set(timeZone, found);
  }
  return found;
}

type Fields = { year: number; month: number; day: number; hour: number; minute: number; second: number };

function fieldsAt(instant: number, timeZone: string): Fields {
  const parts: Record<string, number> = {};
  for (const part of formatter(timeZone).formatToParts(new Date(instant))) {
    if (part.type !== "literal") parts[part.type] = Number(part.value);
  }
  return { year: parts.year, month: parts.month, day: parts.day, hour: parts.hour, minute: parts.minute, second: parts.second };
}

/** The offset of a zone from UTC in milliseconds at an instant, positive east of Greenwich. */
function offsetAt(instant: number, timeZone: string): number {
  const f = fieldsAt(instant, timeZone);
  return Date.UTC(f.year, f.month - 1, f.day, f.hour, f.minute, f.second) - Math.floor(instant / 1000) * 1000;
}

const pad = (value: number) => String(value).padStart(2, "0");

/**
 * The instant of a local date and time in a zone, or null when the text is not a real date and time.
 * A time that occurs twice resolves to the earlier instant. A time that does not exist resolves
 * with the offset that applies before the clock change, which lands one hour later on the wall clock.
 */
export function localToUtc({ date, time }: LocalDateTime, timeZone: string): Date | null {
  const d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date.trim());
  const t = /^(\d{2}):(\d{2})$/.exec(time.trim());
  if (!d || !t) return null;
  const [year, month, day, hour, minute] = [Number(d[1]), Number(d[2]), Number(d[3]), Number(t[1]), Number(t[2])];
  if (hour > 23 || minute > 59) return null;
  const wall = Date.UTC(year, month - 1, day, hour, minute);
  const check = new Date(wall);
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  const before = offsetAt(wall - DAY_MS, timeZone);
  const after = offsetAt(wall + DAY_MS, timeZone);
  const candidates = [...new Set([before, after])].map(offset => wall - offset).filter(instant => wall - offsetAt(instant, timeZone) === instant);
  return new Date(candidates.length > 0 ? Math.min(...candidates) : wall - before);
}

/** The local date and time of an instant in a zone, as a chosen date and time. */
export function customFromInstant(instant: Date, timeZone: string): { kind: "custom" } & LocalDateTime {
  const f = fieldsAt(instant.getTime(), timeZone);
  return { kind: "custom", date: `${f.year}-${pad(f.month)}-${pad(f.day)}`, time: `${pad(f.hour)}:${pad(f.minute)}` };
}

/** The choice an entry edit starts with. */
export const whenFromEntry = (): WhenChoice => ({ kind: "keep" });

/**
 * The occurred time to send for a choice: null when the server's own time applies (now) or the entry
 * keeps its time. Relative days keep the local time of day. A time later than now or not a real date
 * and time carries an error.
 */
export function resolveWhen(choice: WhenChoice, now: Date, timeZone: string): ResolvedWhen {
  if (choice.kind === "now" || choice.kind === "keep") return { occurredAt: null, error: null };
  let instant: Date | null;
  if (choice.kind === "custom") instant = localToUtc(choice, timeZone);
  else {
    const today = customFromInstant(now, timeZone);
    const [year, month, day] = today.date.split("-").map(Number);
    const shifted = new Date(Date.UTC(year, month - 1, day - (choice.kind === "yesterday" ? 1 : 2)));
    const date = `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`;
    instant = localToUtc({ date, time: today.time }, timeZone);
  }
  if (!instant) return { occurredAt: null, error: "invalid" };
  if (instant.getTime() > now.getTime()) return { occurredAt: null, error: "future" };
  return { occurredAt: instant.toISOString(), error: null };
}

/** The time zone of the device. */
export const deviceTimeZone = (): string => Intl.DateTimeFormat().resolvedOptions().timeZone;
