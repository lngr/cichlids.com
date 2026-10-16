import type { ProfileDetail, ProfileImageRole } from "@cichlids/client-core";

/** The roles in the order the editor shows them. */
export const PROFILE_IMAGE_ROLES: ProfileImageRole[] = ["avatar", "header"];

/** The side of the round avatar preview in the editor. */
export const AVATAR_PREVIEW_SIZE = 96;

/** The width to height ratio of the header preview and of the header on the profile. */
export const HEADER_RATIO = 3;

/** The URL of the avatar or the header of a profile, or null when the profile has none. */
export function profileImageUrl(profile: Pick<ProfileDetail, "avatarUrl" | "profileImageUrl">, role: ProfileImageRole): string | null {
  return (role === "avatar" ? profile.avatarUrl : profile.profileImageUrl) ?? null;
}

/** The translation key of the hint shown while the role has no image. */
export function profileImageNoneKey(role: ProfileImageRole): "profileImages.avatarNone" | "profileImages.headerNone" {
  return role === "avatar" ? "profileImages.avatarNone" : "profileImages.headerNone";
}
