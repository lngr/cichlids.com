import { describe, expect, it } from "vitest";
import {
  COMMENT_MAX_LENGTH,
  MAX_STARS,
  STAR_VALUES,
  canSubmit,
  commentLength,
  isOverCommentLimit,
  emptyCommentDraft,
  offersStars,
  starsOf,
  toCreateCommentRequest,
  toggleStar,
} from "./commentComposerState";

describe("emptyCommentDraft", () => {
  it("starts without text and without stars", () => {
    expect(emptyCommentDraft()).toEqual({ body: "", stars: null });
  });

  it("offers the stars 1 to 5", () => {
    expect(MAX_STARS).toBe(5);
    expect(STAR_VALUES).toEqual([1, 2, 3, 4, 5]);
  });
});

describe("canSubmit", () => {
  it("rejects an empty draft", () => {
    expect(canSubmit(emptyCommentDraft())).toBe(false);
  });

  it("rejects text of whitespace only", () => {
    expect(canSubmit({ body: "  \n\t ", stars: null })).toBe(false);
  });

  it("accepts text without stars", () => {
    expect(canSubmit({ body: "Nice fish", stars: null })).toBe(true);
  });

  it("accepts stars without text", () => {
    expect(canSubmit({ body: "", stars: 3 })).toBe(true);
  });

  it("accepts text with stars", () => {
    expect(canSubmit({ body: "Nice fish", stars: 5 })).toBe(true);
  });
});

describe("toggleStar", () => {
  it("sets the tapped star when none is selected", () => {
    expect(toggleStar(null, 4)).toBe(4);
  });

  it("switches to another tapped star", () => {
    expect(toggleStar(4, 2)).toBe(2);
  });

  it("clears the selection when the selected star is tapped", () => {
    expect(toggleStar(3, 3)).toBeNull();
  });
});

describe("toCreateCommentRequest", () => {
  it("sends the trimmed text and the stars", () => {
    expect(toCreateCommentRequest({ body: "  Nice fish \n", stars: 4 })).toEqual({ body: "Nice fish", stars: 4 });
  });

  it("sends null for text of whitespace only", () => {
    expect(toCreateCommentRequest({ body: "   ", stars: 2 })).toEqual({ body: null, stars: 2 });
  });

  it("sends null for missing stars", () => {
    expect(toCreateCommentRequest({ body: "Nice fish", stars: null })).toEqual({ body: "Nice fish", stars: null });
  });
});

describe("starsOf", () => {
  it("reads the stars of a rated comment", () => {
    expect(starsOf({ stars: 4 })).toBe(4);
  });

  it("reads stars the API sends as a string", () => {
    expect(starsOf({ stars: "3" })).toBe(3);
  });

  it("returns 0 for a comment without a rating", () => {
    expect(starsOf({ stars: null })).toBe(0);
    expect(starsOf({})).toBe(0);
  });

  it("keeps the value within 0 to 5", () => {
    expect(starsOf({ stars: 9 })).toBe(5);
    expect(starsOf({ stars: -1 })).toBe(0);
    expect(starsOf({ stars: "x" })).toBe(0);
  });
});

describe("offersStars", () => {
  it("offers stars on content of another member", () => {
    expect(offersStars(7, 9)).toBe(true);
  });

  it("offers no stars on own content", () => {
    expect(offersStars(7, 7)).toBe(false);
  });

  it("compares ids that arrive as numbers or as strings", () => {
    expect(offersStars("7", 7)).toBe(false);
    expect(offersStars(7, "7")).toBe(false);
    expect(offersStars("7", "9")).toBe(true);
  });

  it("offers stars while the signed-in profile is unknown", () => {
    expect(offersStars(7, null)).toBe(true);
  });
});

describe("comment length limit", () => {
  it("allows 10000 characters", () => {
    expect(COMMENT_MAX_LENGTH).toBe(10000);
  });

  it("counts the characters of the trimmed text", () => {
    expect(commentLength({ body: "  abc \n", stars: null })).toBe(3);
    expect(commentLength(emptyCommentDraft())).toBe(0);
  });

  it("accepts a comment of exactly the maximum length", () => {
    const draft = { body: "a".repeat(10000), stars: null };
    expect(isOverCommentLimit(draft)).toBe(false);
    expect(canSubmit(draft)).toBe(true);
  });

  it("measures the limit after trimming", () => {
    expect(isOverCommentLimit({ body: ` ${"a".repeat(10000)} `, stars: null })).toBe(false);
  });

  it("blocks sending a comment above the maximum length, also with stars", () => {
    const draft = { body: "a".repeat(10001), stars: 4 };
    expect(isOverCommentLimit(draft)).toBe(true);
    expect(canSubmit(draft)).toBe(false);
  });
});
