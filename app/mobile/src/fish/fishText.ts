import type { FishGrowth, FishLengthUnit, FishPage, FishTimelineItem, GrowthProblem } from "@cichlids/client-core";
import { formatLocalDate } from "../streaks/streakText";
import { formatMeasurement, type UnitSystem } from "../units/format";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The length unit of a unit system: centimetres for metric, inches for US. */
export const lengthUnitOf = (system: UnitSystem): FishLengthUnit => (system === "us" ? "inch" : "cm");

/**
 * A measured length in the viewer's units. The owner reads the value and unit typed, with the precision typed;
 * everybody else reads the length converted to their own unit system.
 */
export function lengthText(growth: FishGrowth, system: UnitSystem, language: string, owner: boolean): string {
  return formatMeasurement(growth.lengthCm, "length", system, language, { value: growth.inputValue, system: growth.inputUnit === "inch" ? "us" : "metric" }, owner);
}

/** The label of a pure-bred generation: F1, F2 and so on. */
export const generationLabel = (generation: number): string => `F${generation}`;

/** The text of a timeline item. */
export function timelineText(item: FishTimelineItem, owner: boolean, system: UnitSystem, t: Translate, language: string): string {
  switch (item.kind) {
    case "moved_in": return t("fish.event.movedIn");
    case "growth": return item.growth ? t("fish.event.growth", { length: lengthText(item.growth, system, language, owner) }) : "";
    case "spawning":
      return item.spawning?.hybrid === false && item.spawning.generation !== null
        ? t("fish.event.spawning", { generation: generationLabel(item.spawning.generation) })
        : t("fish.event.spawningHybrid");
    case "moved": {
      const to = item.move?.toTank;
      return to ? t("fish.event.moved", { tank: to.title || t("common.untitled") }) : t("fish.event.movedAway");
    }
    case "died": return t("fish.event.died");
    case "left": return t(`fish.event.left.${item.reason ?? "moved"}`);
    case "anniversary": return t("fish.event.anniversary", { count: item.year ?? 0 });
  }
}

/** The line a screen reader announces for a timeline item: its day and its text. */
export const timelineLabel = (item: FishTimelineItem, owner: boolean, system: UnitSystem, t: Translate, language: string): string =>
  `${formatLocalDate(item.on, language)}: ${timelineText(item, owner, system, t, language)}`;

/** Whether the owner can delete the item: growth and spawning entries. */
export const deletableItem = (item: FishTimelineItem): boolean => item.eventId !== null && (item.kind === "growth" || item.kind === "spawning");

/** The dates of a memorial: the days it moved in and died, or only the day it died when it moved in before the tank was recorded. */
export function memorialText(page: FishPage, t: Translate, language: string): string {
  const died = page.removedOn === null ? null : formatLocalDate(page.removedOn, language);
  if (died === null) return "";
  return page.movedInOn === null
    ? t("fish.memorialDied", { date: died })
    : t("fish.memorialDates", { from: formatLocalDate(page.movedInOn, language), to: died });
}

/** The line about the tank the fish lives in, or lived in last; a deleted tank says so. */
export function tankText(page: FishPage, t: Translate): string {
  const title = page.tank.title || t("common.untitled");
  const name = page.tank.deleted ? t("fish.tankDeleted", { tank: title }) : title;
  return t(page.removedOn === null ? "fish.lives" : "fish.lived", { tank: name });
}

/** The translation key of the first problem of the growth form. */
export function growthProblemKey(problem: GrowthProblem): string {
  return problem === "length" ? "fish.errors.length" : "fish.errors.lengthTooLarge";
}
