import type { InhabitantProblem, StockedInhabitant } from "@cichlids/client-core";
import { formatLocalDate } from "../streaks/streakText";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** When an inhabitant moved in: its day, or the creation of the tank for a migrated inhabitant. */
export function movedInText(inhabitant: StockedInhabitant, t: Translate, language: string): string {
  return inhabitant.movedInOn === null
    ? t("stocking.sinceCreated")
    : t("stocking.since", { date: formatLocalDate(inhabitant.movedInOn, language) });
}

/** When and why an inhabitant left; null for one that lives in the tank. */
export function leftText(inhabitant: StockedInhabitant, t: Translate, language: string): string | null {
  if (inhabitant.removedOn === null || inhabitant.removalReason === null) return null;
  return t(`stocking.left.${inhabitant.removalReason}`, { date: formatLocalDate(inhabitant.removedOn, language) });
}

/** The count of a group in the language; null for a named fish, "?" when the count is unknown. */
export function countText(inhabitant: StockedInhabitant, language: string, unknown: string): string | null {
  if (inhabitant.name !== null) return null;
  return inhabitant.count === null ? unknown : new Intl.NumberFormat(language).format(inhabitant.count);
}

/** The translation key of the first problem of the inhabitant form. */
export function problemKey(problem: InhabitantProblem): string {
  switch (problem) {
    case "species": return "stocking.errors.species";
    case "count": return "stocking.errors.count";
    case "name": return "stocking.errors.name";
    case "name_too_long": return "stocking.errors.nameTooLong";
    case "moved_in_invalid": return "stocking.errors.movedInInvalid";
    case "moved_in_future": return "stocking.errors.movedInFuture";
  }
}

/** The translation key of a problem with the day of a removal; null when the day is fine. */
export function removalProblemKey(check: "ok" | "invalid" | "future" | "before_moved_in"): string | null {
  switch (check) {
    case "ok": return null;
    case "invalid": return "stocking.errors.removedInvalid";
    case "future": return "stocking.errors.removedFuture";
    case "before_moved_in": return "stocking.errors.removedBefore";
  }
}

/** The line a screen reader announces for an inhabitant: label, count and the day it moved in. */
export function inhabitantSummary(inhabitant: StockedInhabitant, label: string, t: Translate, language: string, unknown: string): string {
  const count = countText(inhabitant, language, unknown);
  return [label, count, movedInText(inhabitant, t, language)].filter(Boolean).join(", ");
}
