import type { WaterChangeStage } from "@cichlids/client-core";

/** The stage of a snapshot value of the water change machine, or null outside the stages. */
export function stageOfValue(value: unknown): WaterChangeStage | null {
  const running = (value as { running?: string | Record<string, string> } | null)?.running;
  if (!running) return null;
  return (typeof running === "string" ? running : Object.keys(running)[0]) as WaterChangeStage;
}

/** The translation key of the evidence state of a water change. */
export const evidenceKey = (state: "none" | "partial" | "verified") => `waterChange.evidence.${state}` as const;

/** The translation key of a finding that keeps a water change from being verified; null for an unknown code. */
export function missingKey(code: string): string | null {
  return code === "draining_missing" || code === "duration_too_short" || code === "photo_missing" ? `waterChange.missing.${code}` : null;
}
