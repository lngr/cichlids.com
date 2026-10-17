import type { AdoptTankResultDto, ImageUrlsDto, PhotoSuggestionDto } from "./generated/types.gen.js";

/**
 * A migrated photo of the member that can be assigned to an archive tank. date is the moment the
 * photo is placed at (capture time, otherwise the time of its story, otherwise of the file);
 * takenAt is the capture time and is null without one.
 */
export type PhotoSuggestion = { mediaItemId: number; image: ImageUrlsDto | null; takenAt: string | null; date: string };

/** The adoption of an archive tank. badge names a badge the adoption awarded and is null otherwise. */
export type TankAdoption = { tankId: number; title: string; badge: string | null };

export const toPhotoSuggestion = (dto: PhotoSuggestionDto): PhotoSuggestion => ({
  mediaItemId: Number(dto.mediaItemId),
  image: dto.image,
  takenAt: dto.takenAt,
  date: dto.date,
});

export const toTankAdoption = (dto: AdoptTankResultDto): TankAdoption => ({ tankId: Number(dto.tankId), title: dto.title, badge: dto.badge });

/** The number of photos one adoption accepts at most. */
export const ADOPTION_MAX_PHOTOS = 100;

/**
 * The chosen photos after a tap on one suggestion: a chosen photo leaves the choice, any other
 * joins at the end, so the order of the choice is the order of the taps. The choice stops growing
 * at ADOPTION_MAX_PHOTOS.
 */
export function toggleSuggestion(chosen: readonly number[], mediaItemId: number): number[] {
  if (chosen.includes(mediaItemId)) return chosen.filter(id => id !== mediaItemId);
  return chosen.length >= ADOPTION_MAX_PHOTOS ? [...chosen] : [...chosen, mediaItemId];
}

/** The reason the API refused an adoption. */
export type AdoptionProblem = "already_adopted" | "tank_not_found" | "photo_not_found" | "invalid";

const PROBLEM_OF_CODE: Readonly<Record<string, AdoptionProblem>> = {
  tank_already_adopted: "already_adopted",
  legacy_tank_not_found: "tank_not_found",
  media_not_found: "photo_not_found",
  media_already_linked: "invalid",
  adoption_photos_invalid: "invalid",
};

/** The refusal an error of an adoption or of the photo suggestions carries, or null for any other error. */
export function adoptionProblemOf(error: unknown): AdoptionProblem | null {
  if (typeof error !== "object" || error === null) return null;
  const { code } = error as { code?: unknown };
  return typeof code === "string" ? PROBLEM_OF_CODE[code] ?? null : null;
}
