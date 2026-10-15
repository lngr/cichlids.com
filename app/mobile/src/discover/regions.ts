import { selectableRegions, type SelectableRegion } from "../onboarding/standardTanks";

export function orderDiscoverRegions(interests: readonly string[]): SelectableRegion[] {
  const preferred = interests.filter((region): region is SelectableRegion => selectableRegions.some(value => value === region));
  return [...new Set([...preferred, ...selectableRegions])];
}
