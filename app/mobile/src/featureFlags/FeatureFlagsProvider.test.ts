import { afterEach, describe, expect, it, vi } from "vitest";
const harness = vi.hoisted(() => ({
  effect: undefined as undefined | (() => (() => void)),
  platform: "web",
  load: vi.fn(),
  nativeListener: undefined as undefined | ((state: string) => void),
  remove: vi.fn(),
}));
vi.mock("react", () => ({
  useEffect: (effect: () => (() => void)) => { harness.effect = effect; },
  useSyncExternalStore: (_subscribe: unknown, snapshot: () => boolean) => snapshot(),
}));
vi.mock("react-native", () => ({
  Platform: { get OS() { return harness.platform; } },
  AppState: { addEventListener: (_event: string, listener: (state: string) => void) => {
    harness.nativeListener = listener;
    return { remove: harness.remove };
  } },
}));
vi.mock("../api/client", () => ({ apiClient: { featureFlags: { get: harness.load } } }));
import { FeatureFlagsProvider, useFeatureFlag } from "./FeatureFlagsProvider";

afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });

describe("feature flag lifecycle and hook", () => {
  it("refreshes the web hook on visible foreground returns and removes the listener", async () => {
    harness.platform = "web";
    const document = { visibilityState: "hidden", addEventListener: vi.fn(), removeEventListener: vi.fn() };
    vi.stubGlobal("document", document);
    harness.load.mockResolvedValue({ "discover-activity": true });
    expect(FeatureFlagsProvider({ children: "app" })).toBe("app");
    const stop = harness.effect!();
    await vi.waitFor(() => expect(useFeatureFlag("discover-activity")).toBe(true));
    const [event, listener] = document.addEventListener.mock.calls[0];
    expect(event).toBe("visibilitychange");
    listener();
    expect(harness.load).toHaveBeenCalledTimes(1);
    document.visibilityState = "visible";
    harness.load.mockRejectedValue(new Error("offline"));
    listener();
    await vi.waitFor(() => expect(useFeatureFlag("discover-activity")).toBe(false));
    stop();
    expect(document.removeEventListener).toHaveBeenCalledWith("visibilitychange", listener);
  });

  it("refreshes Android and iOS flags on active and removes the native subscription", async () => {
    for (const platform of ["android", "ios"]) {
      harness.platform = platform;
      harness.load.mockResolvedValue({ "discover-activity": false });
      FeatureFlagsProvider({ children: null });
      const stop = harness.effect!();
      await Promise.resolve();
      const before = harness.load.mock.calls.length;
      harness.nativeListener!("background");
      expect(harness.load).toHaveBeenCalledTimes(before);
      harness.load.mockResolvedValue({ "discover-activity": true });
      harness.nativeListener!("active");
      await vi.waitFor(() => expect(useFeatureFlag("discover-activity")).toBe(true));
      stop();
    }
    expect(harness.remove).toHaveBeenCalledTimes(2);
  });
});
