import { useEffect, useSyncExternalStore, type ReactNode } from "react";
import { AppState, Platform } from "react-native";
import { apiClient } from "../api/client";
import { createFeatureFlagStore, startFeatureFlags, type FeatureFlagName } from "./featureFlagStore";

const store = createFeatureFlagStore(() => apiClient.featureFlags.get());

export function FeatureFlagsProvider({ children }: { children: ReactNode }) {
  useEffect(() => startFeatureFlags(store, (refresh) => {
    if (Platform.OS === "web" && typeof document !== "undefined") {
      const onVisibility = () => { if (document.visibilityState === "visible") refresh(); };
      document.addEventListener("visibilitychange", onVisibility);
      return () => document.removeEventListener("visibilitychange", onVisibility);
    }
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") refresh();
    });
    return () => subscription.remove();
  }), []);
  return children;
}

export function useFeatureFlag(name: FeatureFlagName): boolean {
  return useSyncExternalStore(store.subscribe, () => store.enabled(name), () => false);
}
