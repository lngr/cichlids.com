/**
 * Whether the time of a care entry is fixed. An entry created by the guided water change mode
 * keeps the time the mode recorded; every other entry can be corrected.
 */
export function isTimeFixed(entry: { waterChangeSessionId?: number | string | null }): boolean {
  return entry.waterChangeSessionId !== null && entry.waterChangeSessionId !== undefined;
}
