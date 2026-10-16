import { describe, expect, it, vi } from "vitest";
import { createRefreshSignal } from "./refreshSignal";

describe("refreshSignal", () => {
  it("tells every subscriber and stops after unsubscribing", () => {
    const signal = createRefreshSignal();
    const first = vi.fn();
    const second = vi.fn();
    const stop = signal.subscribe(first);
    signal.subscribe(second);
    signal.request();
    stop();
    signal.request();
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(2);
  });
});
