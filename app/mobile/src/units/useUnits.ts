import { useEffect, useSyncExternalStore } from "react";
import { AppState } from "react-native";
import { ensureUnitsInitialized, getUnitSystem, getUnitsSnapshot, refreshDeviceUnits, subscribeUnits } from "./preferences";

export function useUnits() {
  useSyncExternalStore(subscribeUnits, getUnitsSnapshot, getUnitsSnapshot);
  useEffect(() => {
    void ensureUnitsInitialized();
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") refreshDeviceUnits();
    });
    return () => subscription.remove();
  }, []);
  return getUnitSystem();
}
