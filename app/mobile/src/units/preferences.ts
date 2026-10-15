import * as Localization from "expo-localization";
import { localStore } from "../lib/localStore";
import { unitsForRegion, type UnitSystem } from "./format";

export const UNITS_STORAGE_KEY = "cichlids.units";
let preference: UnitSystem | null = null;
let system = unitsForRegion(Localization.getLocales()[0]?.regionCode);
const listeners = new Set<() => void>();
let initialization: Promise<void> | undefined;
export const getUnitSystem = () => system;
export const getUnitPreference = () => preference;
export const getUnitsSnapshot = () => `${preference ?? "device"}:${system}`;
export function subscribeUnits(listener: () => void): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
export function refreshDeviceUnits(): void {
  system = preference ?? unitsForRegion(Localization.getLocales()[0]?.regionCode);
  listeners.forEach((listener) => listener());
}
export async function initializeUnits(): Promise<void> {
  let stored: string | null = null;
  try { stored = await localStore.getItem(UNITS_STORAGE_KEY); } catch { /* Device region supplies the default. */ }
  preference = stored === "metric" || stored === "us" ? stored : null;
  refreshDeviceUnits();
}
export function ensureUnitsInitialized(): Promise<void> {
  return initialization ??= initializeUnits();
}
export async function setUnitPreference(value: UnitSystem | null): Promise<void> {
  if (value === null) await localStore.removeItem(UNITS_STORAGE_KEY);
  else await localStore.setItem(UNITS_STORAGE_KEY, value);
  preference = value;
  refreshDeviceUnits();
}
