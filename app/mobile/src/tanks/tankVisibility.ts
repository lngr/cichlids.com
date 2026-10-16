import type { TankVisibilityDto } from "@cichlids/client-core";

export type Levels = Pick<TankVisibilityDto, "profile" | "stream">;

/** The levels after the owner picks a profile level; a private profile takes the stream with it. */
export function draftAfterProfileChange(draft: Levels, profile: Levels["profile"]): Levels {
  return { profile, stream: profile === "private" ? "private" : draft.stream };
}

/** The levels after the owner picks a stream level; a stream is never wider than the profile. */
export function draftAfterStreamChange(draft: Levels, stream: Levels["stream"]): Levels {
  return draft.profile === "private" && stream === "public" ? draft : { ...draft, stream };
}

export function isDraftChanged(current: Levels, draft: Levels): boolean {
  return current.profile !== draft.profile || current.stream !== draft.stream;
}

/** Whether saving the draft makes the tank profile public, which the owner confirms with the explanation. */
export function requiresConsentText(current: Levels, draft: Levels): boolean {
  return current.profile !== "public" && draft.profile === "public";
}

/** The badge an owner sees on the tank: a private tank, a public tank with a private stream, or none. */
export function badgeFor(levels: Levels): "private" | "streamPrivate" | null {
  if (levels.profile === "private") return "private";
  return levels.stream === "private" ? "streamPrivate" : null;
}
