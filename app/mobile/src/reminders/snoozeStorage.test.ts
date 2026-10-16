import { describe, expect, it } from "vitest";
import { parseSnoozes, serializeSnoozes } from "./snoozeStorage";

describe("snoozeStorage", () => {
  const snooze = { tankId: 4, kind: "water_change" as const, dueDate: "2026-10-15", until: "2026-10-16" };
  it("round-trips snoozes", () => {
    expect(parseSnoozes(serializeSnoozes([snooze]))).toEqual([snooze]);
  });
  it("reads nothing from missing or damaged text", () => {
    expect(parseSnoozes(null)).toEqual([]);
    expect(parseSnoozes("")).toEqual([]);
    expect(parseSnoozes("{")).toEqual([]);
    expect(parseSnoozes("{\"a\":1}")).toEqual([]);
  });
  it("skips entries that do not have the shape of a snooze", () => {
    const damaged = [snooze, { tankId: "4", kind: "water_change", dueDate: "2026-10-15", until: "2026-10-16" }, { tankId: 4, kind: "feeding", dueDate: "2026-10-15", until: "2026-10-16" },
      { tankId: 4, kind: "measurement", dueDate: "tomorrow", until: "2026-10-16" }, null, 7];
    expect(parseSnoozes(JSON.stringify(damaged))).toEqual([snooze]);
  });
});
