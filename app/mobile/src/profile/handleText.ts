import { handleProblemOf } from "@cichlids/client-core";
import { formatDate } from "../lib/format";
import { translateApiError } from "../lib/apiErrors";

/**
 * The text at the handle field for a failed change: the reason the API named, with the date of the
 * next change when the member changed the handle too recently, and the general error text for any
 * other failure.
 */
export function handleErrorText(error: unknown, t: (key: string, options?: Record<string, string>) => string, language: string): string {
  const problem = handleProblemOf(error);
  if (problem?.reason === "too_soon" && problem.nextChangeAt) {
    return t("me.handle.tooSoon", { date: formatDate(problem.nextChangeAt, language) });
  }
  return translateApiError(error, t, "common.loadError");
}

/** The line that names the day of the next possible change, or null when a change is possible now. */
export function nextChangeText(availableAt: string | null | undefined, now: Date, t: (key: string, options?: Record<string, string>) => string, language: string): string | null {
  if (!availableAt) return null;
  const at = new Date(availableAt);
  if (Number.isNaN(at.getTime()) || at <= now) return null;
  return t("me.handle.nextChange", { date: formatDate(availableAt, language) });
}

/** Whether the typed text can be sent: it holds something and differs from the current handle apart from surrounding blanks. */
export function canSubmitHandle(input: string, current: string): boolean {
  const typed = input.trim();
  return typed !== "" && typed !== current;
}
