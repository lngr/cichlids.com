import type { CareHeatmap, CareHeatmapDay } from "@cichlids/client-core";
import { formatLocalDate } from "../streaks/streakText";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** One sentence group a screen reader reads for the whole grid: the care days, the last water change and the decommissioned days. */
export function heatmapSummary(heatmap: CareHeatmap, t: Translate, language: string): string {
  const parts = heatmap.activeDays === 0 ? [t("careHeatmap.none")] : [t("careHeatmap.summary", { count: heatmap.activeDays })];
  if (heatmap.activeDays > 0 && heatmap.lastWaterChange) parts.push(t("careHeatmap.lastWaterChange", { date: formatLocalDate(heatmap.lastWaterChange, language) }));
  if (heatmap.decommissionedDays.length > 0) parts.push(t("careHeatmap.rest", { count: heatmap.decommissionedDays.length }));
  return parts.join(". ");
}

/** One line of the list fallback: the date with the counts of the kinds the day holds. */
export function heatmapItemText(day: CareHeatmapDay, t: Translate, language: string): string {
  const counts = [
    day.waterChanges > 0 ? t("careHeatmap.waterChanges", { count: day.waterChanges }) : null,
    day.measurements > 0 ? t("careHeatmap.measurements", { count: day.measurements }) : null,
  ].filter((part): part is string => part !== null);
  return `${formatLocalDate(day.date, language)}: ${counts.join(", ")}`;
}
