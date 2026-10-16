import type { TankVisibilityDto } from "@cichlids/client-core";

export type Levels = Pick<TankVisibilityDto, "profile" | "stream">;
export type Level = Levels["profile"];

/** The levels from the widest to the narrowest audience. */
export const LEVELS: readonly Level[] = ["public", "followers", "selected", "private"];

const BREADTH: Record<Level, number> = { private: 0, selected: 1, followers: 2, public: 3 };

/** Whether the level reaches more viewers than the other level. */
export function isWider(level: Level, other: Level): boolean {
  return BREADTH[level] > BREADTH[other];
}

/** The levels after the owner picks a profile level; a stream wider than the profile narrows to the profile. */
export function draftAfterProfileChange(draft: Levels, profile: Level): Levels {
  return { profile, stream: isWider(draft.stream, profile) ? profile : draft.stream };
}

/** The levels after the owner picks a stream level; a stream is never wider than the profile. */
export function draftAfterStreamChange(draft: Levels, stream: Level): Levels {
  return isWider(stream, draft.profile) ? draft : { ...draft, stream };
}

export function isDraftChanged(current: Levels, draft: Levels): boolean {
  return current.profile !== draft.profile || current.stream !== draft.stream;
}

/** Whether saving the draft makes the tank profile public, which the owner confirms with the explanation. */
export function requiresConsentText(current: Levels, draft: Levels): boolean {
  return current.profile !== "public" && draft.profile === "public";
}

export type VisibilityBadge = "private" | "selected" | "followers" | "streamPrivate" | "streamSelected" | "streamFollowers";

/** The badge an owner sees on a tank that is not fully public: the narrowest layer decides, the profile first. */
export function badgeFor(levels: Levels): VisibilityBadge | null {
  if (levels.profile !== "public") return levels.profile;
  switch (levels.stream) {
    case "private": return "streamPrivate";
    case "selected": return "streamSelected";
    case "followers": return "streamFollowers";
    default: return null;
  }
}
