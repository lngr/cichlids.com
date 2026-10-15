export type FeatureFlagName = "discover-activity";

export function createFeatureFlagStore(load: () => Promise<unknown>) {
  let flags: Record<FeatureFlagName, boolean> = { "discover-activity": false };
  let version = 0;
  const listeners = new Set<() => void>();
  return {
    enabled: (name: FeatureFlagName) => flags[name] === true,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    cancel() { version++; },
    async refresh() {
      const request = ++version;
      let next = false;
      try {
        const result = await load();
        next = typeof result === "object" && result !== null
          && "discover-activity" in result && result["discover-activity"] === true;
      } catch {
        next = false;
      }
      if (request !== version) return;
      flags = { "discover-activity": next };
      listeners.forEach((listener) => listener());
    },
  };
}

export function startFeatureFlags(
  store: ReturnType<typeof createFeatureFlagStore>,
  subscribeForeground: (refresh: () => void) => () => void,
): () => void {
  const refresh = () => { void store.refresh(); };
  const unsubscribe = subscribeForeground(refresh);
  refresh();
  return () => { unsubscribe(); store.cancel(); };
}
