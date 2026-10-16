import { MAX_PHOTOS, type CareEntry, type CarePhoto } from "@cichlids/client-core";

const ORDER = {
  thumb: ["thumb", "small", "medium", "large"],
  large: ["large", "medium", "small", "thumb"],
} as const;

/** The path of the photo variant that fits a size best: the requested one, else the nearest that exists. */
export function photoPath(photo: CarePhoto, size: keyof typeof ORDER): string | null {
  for (const label of ORDER[size]) {
    const path = photo.urls[label];
    if (path) return path;
  }
  return null;
}

/** How many more photos an entry takes. */
export const remainingPhotoSlots = (entry: CareEntry): number => Math.max(0, MAX_PHOTOS - entry.photos.length);

export const canAddPhoto = (entry: CareEntry): boolean => remainingPhotoSlots(entry) > 0;
