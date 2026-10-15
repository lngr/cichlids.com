import { describe, expect, it, vi } from "vitest";
import { createFeatureFlagStore, startFeatureFlags } from "./featureFlagStore";

describe("feature flags", () => {
  it("starts off, loads at startup and foreground, and unsubscribes on unmount", async () => {
    const load = vi.fn().mockResolvedValue({ "discover-activity": true });
    const store = createFeatureFlagStore(load);
    expect(store.enabled("discover-activity")).toBe(false);
    let foreground = () => {};
    const unsubscribe = vi.fn();
    const stop = startFeatureFlags(store, (callback) => { foreground = callback; return unsubscribe; });
    await vi.waitFor(() => expect(store.enabled("discover-activity")).toBe(true));
    load.mockResolvedValue({ "discover-activity": false });
    foreground();
    await vi.waitFor(() => expect(store.enabled("discover-activity")).toBe(false));
    expect(load).toHaveBeenCalledTimes(2);
    stop();
    expect(unsubscribe).toHaveBeenCalledOnce();
  });

  it("fails closed after an enabled flag on network errors or malformed responses", async () => {
    const load = vi.fn().mockResolvedValue({ "discover-activity": true });
    const store = createFeatureFlagStore(load);
    const listener = vi.fn();
    store.subscribe(listener);
    for (const response of [null, {}, { "discover-activity": "true" }, { "discover-activity": 1 }]) {
      load.mockResolvedValueOnce({ "discover-activity": true }).mockResolvedValueOnce(response);
      await store.refresh();
      expect(store.enabled("discover-activity")).toBe(true);
      await store.refresh();
      expect(store.enabled("discover-activity")).toBe(false);
    }
    load.mockRejectedValueOnce(new Error("offline"));
    await store.refresh();
    expect(store.enabled("discover-activity")).toBe(false);
    expect(listener).toHaveBeenCalled();
  });

  it("ignores an older successful request after the newest request fails", async () => {
    let resolve!: (value: unknown) => void;
    const load = vi.fn().mockImplementationOnce(() => new Promise((done) => { resolve = done; }))
      .mockRejectedValueOnce(new Error("offline"));
    const store = createFeatureFlagStore(load);
    const old = store.refresh();
    await store.refresh();
    resolve({ "discover-activity": true });
    await old;
    expect(store.enabled("discover-activity")).toBe(false);
  });

  it("discards requests still pending when the lifecycle stops", async () => {
    let resolve!: (value: unknown) => void;
    const store = createFeatureFlagStore(() => new Promise((done) => { resolve = done; }));
    const stop = startFeatureFlags(store, () => () => {});
    stop();
    resolve({ "discover-activity": true });
    await Promise.resolve();
    expect(store.enabled("discover-activity")).toBe(false);
  });
});
