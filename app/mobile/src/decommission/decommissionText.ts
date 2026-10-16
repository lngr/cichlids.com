import { operatingTime, type DecommissionImage, type Decommissioning, type TankOperation } from "@cichlids/client-core";
import { formatLocalDate } from "../streaks/streakText";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The lines under the operating time of a tank: the time in operation and, when there is one, the decommissioned time. */
export function operatingLines(operation: TankOperation, t: Translate): string[] {
  const { unit, count } = operatingTime(operation);
  const lines = [t(`decommission.${unit}`, { count })];
  if (operation.decommissionedDays > 0) lines.push(t("decommission.rest", { count: operation.decommissionedDays }));
  return lines;
}

/** What an active decommissioning says: since when it lasts and what it pauses. */
export function activeLines(item: Decommissioning, t: Translate, language: string): string[] {
  return [t("decommission.since", { date: formatLocalDate(item.startDate, language) }),
    t(item.scope === "all_care" ? "decommission.statusAll" : "decommission.statusWater")];
}

/** The first and the last day of an ended decommissioning. */
export function pastLine(item: Decommissioning, t: Translate, language: string): string {
  return t("decommission.pastLine", { from: formatLocalDate(item.startDate, language), to: formatLocalDate(item.endDate!, language) });
}

/** The address of the size of an image that suits a small picture, or null when the image has no usable size. */
export function imageOf(image: DecommissionImage | null): string | null {
  return image?.small ?? image?.thumb ?? image?.medium ?? image?.large ?? null;
}
