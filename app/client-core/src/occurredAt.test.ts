import { describe, expect, it } from "vitest";
import { customFromInstant, localToUtc, resolveWhen, whenFromEntry, type WhenChoice } from "./occurredAt.js";

const berlin = "Europe/Berlin";
const newYork = "America/New_York";
const now = new Date("2026-10-16T10:30:00Z"); // 12:30 in Berlin (UTC+2)

describe("local time to UTC", () => {
  it("converts a local wall clock time with the offset of that day", () => {
    expect(localToUtc({ date: "2026-10-14", time: "09:30" }, berlin)?.toISOString()).toBe("2026-10-14T07:30:00.000Z");
    expect(localToUtc({ date: "2026-12-14", time: "09:30" }, berlin)?.toISOString()).toBe("2026-12-14T08:30:00.000Z");
    expect(localToUtc({ date: "2026-12-14", time: "09:30" }, newYork)?.toISOString()).toBe("2026-12-14T14:30:00.000Z");
  });
  it("keeps the local calendar day around midnight", () => {
    expect(localToUtc({ date: "2026-10-14", time: "00:15" }, berlin)?.toISOString()).toBe("2026-10-13T22:15:00.000Z");
    expect(localToUtc({ date: "2026-10-14", time: "23:45" }, newYork)?.toISOString()).toBe("2026-10-15T03:45:00.000Z");
  });
  it("uses the earlier instant for a time that occurs twice when the clock goes back", () => {
    expect(localToUtc({ date: "2026-10-25", time: "02:30" }, berlin)?.toISOString()).toBe("2026-10-25T00:30:00.000Z");
  });
  it("moves a time that does not exist when the clock goes forward to the following hour", () => {
    expect(localToUtc({ date: "2026-03-29", time: "02:30" }, berlin)?.toISOString()).toBe("2026-03-29T01:30:00.000Z");
  });
  it("refuses unusable dates and times", () => {
    for (const bad of [{ date: "2026-02-30", time: "10:00" }, { date: "14.10.2026", time: "10:00" }, { date: "2026-10-14", time: "24:00" }, { date: "2026-10-14", time: "9:5" }, { date: "", time: "" }])
      expect(localToUtc(bad, berlin)).toBeNull();
  });
});

describe("the chosen time", () => {
  const resolve = (choice: WhenChoice, at = now, zone = berlin) => resolveWhen(choice, at, zone);
  it("sends nothing for now and for an unchanged entry", () => {
    expect(resolve({ kind: "now" })).toEqual({ occurredAt: null, error: null });
    expect(resolve({ kind: "keep" })).toEqual({ occurredAt: null, error: null });
  });
  it("puts yesterday and two days ago at the current local time of day", () => {
    expect(resolve({ kind: "yesterday" })).toEqual({ occurredAt: "2026-10-15T10:30:00.000Z", error: null });
    expect(resolve({ kind: "twoDaysAgo" })).toEqual({ occurredAt: "2026-10-14T10:30:00.000Z", error: null });
  });
  it("counts calendar days in the local zone across a clock change", () => {
    const sunday = new Date("2026-10-26T11:30:00Z"); // 12:30 in Berlin after the change
    expect(resolve({ kind: "twoDaysAgo" }, sunday)).toEqual({ occurredAt: "2026-10-24T10:30:00.000Z", error: null });
  });
  it("counts the local day, not the UTC day, shortly after local midnight", () => {
    const late = new Date("2026-10-15T22:30:00Z"); // 00:30 on the 16th in Berlin
    expect(resolve({ kind: "yesterday" }, late)).toEqual({ occurredAt: "2026-10-14T22:30:00.000Z", error: null });
  });
  it("converts a chosen date and time from the local zone", () => {
    expect(resolve({ kind: "custom", date: "2026-10-14", time: "09:30" })).toEqual({ occurredAt: "2026-10-14T07:30:00.000Z", error: null });
  });
  it("refuses a chosen time in the future and an unusable one", () => {
    expect(resolve({ kind: "custom", date: "2026-10-16", time: "12:31" })).toEqual({ occurredAt: null, error: "future" });
    expect(resolve({ kind: "custom", date: "2026-10-16", time: "12:30" }).error).toBeNull();
    expect(resolve({ kind: "custom", date: "tomorrow", time: "12:30" })).toEqual({ occurredAt: null, error: "invalid" });
  });
  it("prefills a custom choice with the local date and time of an instant", () => {
    expect(customFromInstant(now, berlin)).toEqual({ kind: "custom", date: "2026-10-16", time: "12:30" });
    expect(customFromInstant(new Date("2026-10-16T03:30:00Z"), newYork)).toEqual({ kind: "custom", date: "2026-10-15", time: "23:30" });
  });
  it("starts an entry edit on keeping its time", () => {
    expect(whenFromEntry()).toEqual({ kind: "keep" });
  });
});
