import type { StoredWaterChange } from "@cichlids/client-core";

/** The key under which the position of a guided water change is kept on the device. */
export const waterChangeStorageKey = (tankId: number) => `waterChange.${tankId}`;

/**
 * Reads the stored position; a value that is no position counts as none.
 */
export function parseStoredWaterChange(text: string | null): StoredWaterChange | null {
  if (!text) return null;
  try {
    const value = JSON.parse(text) as Partial<StoredWaterChange>;
    return typeof value.sessionId === "number" && typeof value.stage === "string" ? value as StoredWaterChange : null;
  } catch {
    return null;
  }
}
