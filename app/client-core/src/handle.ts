/** Why the API refused a public handle. */
export type HandleProblemReason = "invalid" | "generated" | "reserved" | "taken" | "too_soon";

/**
 * The refusal of a handle change: the reason, and for too_soon the time of the next permitted
 * change as the API names it.
 */
export type HandleProblem = { reason: HandleProblemReason; nextChangeAt?: string };

const REASON_OF_CODE: Readonly<Record<string, HandleProblemReason>> = {
  handle_invalid: "invalid",
  handle_generated: "generated",
  handle_reserved: "reserved",
  handle_taken: "taken",
  handle_change_too_soon: "too_soon",
};

/** The refusal an error of a handle change carries, or null for any other error. */
export function handleProblemOf(error: unknown): HandleProblem | null {
  if (typeof error !== "object" || error === null) return null;
  const { code, problem } = error as { code?: unknown; problem?: Record<string, unknown> };
  const reason = typeof code === "string" ? REASON_OF_CODE[code] : undefined;
  if (reason === undefined) return null;
  const next = problem?.nextChangeAt;
  return reason === "too_soon" && typeof next === "string" ? { reason, nextChangeAt: next } : { reason };
}
