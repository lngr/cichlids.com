import { describe, expect, it } from "vitest";
import { createPublishSignal } from "./publishSignal";

describe("createPublishSignal", () => {
  it("reports no change for a version taken after the last publish", () => {
    const signal = createPublishSignal();
    const seen = signal.version();
    expect(signal.changedSince(seen)).toBe(false);
  });

  it("reports a change once a picture was published after the version was taken", () => {
    const signal = createPublishSignal();
    const seen = signal.version();
    signal.notifyPublished();
    expect(signal.changedSince(seen)).toBe(true);
    expect(signal.changedSince(signal.version())).toBe(false);
  });

  it("keeps separate signals independent", () => {
    const first = createPublishSignal();
    const second = createPublishSignal();
    const seen = second.version();
    first.notifyPublished();
    expect(second.changedSince(seen)).toBe(false);
  });
});
