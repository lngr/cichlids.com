import type { CreateCommentRequest } from "@cichlids/client-core";

/** The highest rating; the star picker offers 1 to MAX_STARS. */
export const MAX_STARS = 5;
export const STAR_VALUES = Array.from({ length: MAX_STARS }, (_, index) => index + 1);

/** What the user has typed and picked in the comment composer; stars is null without a rating. */
export interface CommentDraft {
  body: string;
  stars: number | null;
}

export function emptyCommentDraft(): CommentDraft {
  return { body: "", stars: null };
}

/** A draft can be sent once it has text or a rating (or both). */
export function canSubmit(draft: CommentDraft): boolean {
  return draft.body.trim().length > 0 || draft.stars !== null;
}

/** Tapping a star selects it; tapping the selected star clears the rating. */
export function toggleStar(current: number | null, tapped: number): number | null {
  return current === tapped ? null : tapped;
}

/**
 * The request body for a draft: the trimmed text, or null for text of whitespace only, and the
 * stars, or null without a rating. The API treats a null field as absent.
 */
export function toCreateCommentRequest(draft: CommentDraft): CreateCommentRequest {
  const body = draft.body.trim();
  return { body: body.length > 0 ? body : null, stars: draft.stars };
}

/** The stars (0 to MAX_STARS) of a comment's rating; 0 when the comment has no rating. */
export function starsOf(comment: { stars?: number | string | null }): number {
  const stars = Number(comment.stars ?? 0);
  if (!Number.isFinite(stars)) return 0;
  return Math.min(MAX_STARS, Math.max(0, Math.round(stars)));
}
