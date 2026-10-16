import { daysUntilDeadline, type TimeLapseStreak, type Vacation, type VacationShare, type WaterChangeStreak } from "@cichlids/client-core";
import { resolveLanguage } from "../i18n/locales";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The days of a window that count as "the last year"; a shorter window is the time since joining. */
const YEAR_WINDOW_DAYS = 365;

/** A local calendar date (YYYY-MM-DD) in the language, always on the same day whatever the time zone of the device. */
export function formatLocalDate(date: string, language: string): string {
  const [year, month, day] = date.split("-").map(Number);
  return new Intl.DateTimeFormat(resolveLanguage(language), { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" })
    .format(new Date(Date.UTC(year!, month! - 1, day!)));
}

/** The weeks in a row of the time-lapse streak; calm text for no streak. */
export function timeLapseHeadline(streak: TimeLapseStreak, t: Translate): string {
  return streak.current > 0 ? t("streaks.timeLapse.headline", { count: streak.current }) : t("streaks.none");
}

/** What the week asks for, or null when it asks for nothing. */
export function timeLapseStatus(streak: TimeLapseStreak, t: Translate, language: string): string | null {
  if (streak.paused) return t("streaks.paused");
  if (streak.thisWeekHasPhoto) return t("streaks.timeLapse.done");
  if (!streak.thisWeekOpen) return null;
  return t(streak.current > 0 ? "streaks.timeLapse.openKeep" : "streaks.timeLapse.openStart", { date: formatLocalDate(streak.weekEnd, language) });
}

/** The best run, shown only when it is longer than the current one. */
export function longestText(longest: number, current: number, t: Translate): string | null {
  return longest > current ? t("streaks.longest", { count: longest }) : null;
}

/** The periods in a row of the water change streak; calm text for no streak. */
export function waterChangeHeadline(streak: WaterChangeStreak, t: Translate): string {
  return streak.current > 0 ? t("streaks.waterChange.headline", { count: streak.current }) : t("streaks.none");
}

/** The marker for periods that the guided mode verified; null when none is verified. */
export function verifiedText(streak: WaterChangeStreak, t: Translate): string | null {
  return streak.currentVerified > 0 ? t("streaks.waterChange.verified", { verified: streak.currentVerified, count: streak.current }) : null;
}

/** The state of the streak in calm wording: pause, start, end or the next deadline; null when a viewer sees no deadline. */
export function waterChangeStatus(streak: WaterChangeStreak, today: string, t: Translate, language: string): string | null {
  switch (streak.state) {
    case "paused": return t("streaks.waterChange.paused");
    case "no_history": return t("streaks.waterChange.noHistory");
    case "broken": return t("streaks.waterChange.broken");
    default:
      if (streak.deadline === null) return null;
      return daysUntilDeadline(streak.deadline, today) <= 0
        ? t("streaks.waterChange.deadlineToday")
        : t("streaks.waterChange.deadline", { date: formatLocalDate(streak.deadline, language) });
  }
}

/** The freezes the owner has left in the current tank year; null for a viewer. */
export function freezesText(streak: WaterChangeStreak, t: Translate): string | null {
  return streak.freezesLeft === null ? null : t("streaks.waterChange.freezes", { count: streak.freezesLeft });
}

/** The newest ended vacations as "first day to last day", at most the given number. */
export function pastVacationLines(vacations: readonly Vacation[], t: Translate, language: string, limit: number): string[] {
  return vacations.filter(vacation => vacation.endDate !== null)
    .sort((a, b) => b.startDate.localeCompare(a.startDate))
    .slice(0, limit)
    .map(vacation => t("streaks.vacation.pastLine", { from: formatLocalDate(vacation.startDate, language), to: formatLocalDate(vacation.endDate!, language) }));
}

/** The share of time a member spent on vacation, or null when there is none. */
export function vacationShareText(share: VacationShare, t: Translate, language: string): string | null {
  if (share.vacationDays <= 0) return null;
  const percent = new Intl.NumberFormat(resolveLanguage(language), { style: "percent", maximumFractionDigits: 0 }).format(share.fraction);
  return t(share.windowDays >= YEAR_WINDOW_DAYS ? "streaks.vacation.shareYear" : "streaks.vacation.shareSinceJoining", { percent });
}
