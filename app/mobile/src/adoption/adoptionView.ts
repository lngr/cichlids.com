import type { AdoptionProblem, PhotoSuggestion } from "@cichlids/client-core";
import { resolveLanguage } from "../i18n/locales";

/** The route of the adoption flow of an archive tank. */
export function adoptionRoute(tankId: number): string {
  return `/tanks/${tankId}/adopt`;
}

/** The place of a photo in the choice, counted from 1, or null when the photo is not chosen. */
export function choicePosition(chosen: readonly number[], mediaItemId: number): number | null {
  const index = chosen.indexOf(mediaItemId);
  return index < 0 ? null : index + 1;
}

/** The URL of a grid cell: a small size, since a cell is a fraction of the screen width. */
export function gridImageUrl(image: PhotoSuggestion["image"] | undefined): string | undefined {
  return image?.small ?? image?.thumb ?? image?.medium ?? image?.large ?? undefined;
}

/** The translation key of the message for a refused adoption. */
export function adoptionProblemKey(problem: AdoptionProblem): string {
  switch (problem) {
    case "already_adopted": return "adoption.alreadyAdopted";
    case "tank_not_found": return "adoption.notFound";
    case "photo_not_found": return "adoption.photoNotFound";
    case "invalid": return "adoption.invalid";
  }
}

/** The number of photo columns of the grid for a content width in dp: at least three, cells of at least 96 dp. */
export function gridColumns(width: number): number {
  return Math.max(3, Math.floor(width / 96));
}

/**
 * The date label of a suggestion in the language of the member. A capture time is the wall-clock time of
 * the camera, so its calendar day is the one written in the value; any other date is an instant and shows
 * in the zone of the device.
 */
export function suggestionDate(suggestion: Pick<PhotoSuggestion, "date" | "takenAt">, language: string): string {
  const format = (date: Date, timeZone?: string) => new Intl.DateTimeFormat(resolveLanguage(language),
    { year: "numeric", month: "short", day: "numeric", ...(timeZone ? { timeZone } : {}) }).format(date);
  const written = suggestion.takenAt ? /^(\d{4})-(\d{2})-(\d{2})/.exec(suggestion.takenAt) : null;
  if (written) return format(new Date(Date.UTC(Number(written[1]), Number(written[2]) - 1, Number(written[3]))), "UTC");
  const date = new Date(suggestion.date);
  return Number.isNaN(date.getTime()) ? "" : format(date);
}
