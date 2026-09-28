import { describe, expect, it } from "vitest";
import type { Draft } from "@cichlids/client-core";
import { draftParam, draftPreviewUri, draftThumbnailUri, findDraft, formForDraft } from "./drafts";

function draft(overrides: Partial<Draft> = {}): Draft {
  return {
    id: 7,
    state: "draft",
    topic: "cichlids",
    createdAt: "2026-09-01T10:00:00Z",
    image: { thumb: "t.jpg", small: "s.jpg", medium: "m.jpg", large: "l.jpg", original: "o.jpg" },
    ...overrides,
  };
}

describe("draftParam", () => {
  it("reads a single route parameter", () => {
    expect(draftParam("42")).toBe("42");
  });

  it("takes the first of repeated parameters", () => {
    expect(draftParam(["42", "43"])).toBe("42");
  });

  it("is null without a parameter or with a blank one", () => {
    expect(draftParam(undefined)).toBeNull();
    expect(draftParam("  ")).toBeNull();
    expect(draftParam([])).toBeNull();
  });
});

describe("findDraft", () => {
  it("finds a draft by its id given as text", () => {
    const drafts = [draft({ id: 1 }), draft({ id: 42 })];
    expect(findDraft(drafts, "42")?.id).toBe(42);
  });

  it("matches ids the schema types as text", () => {
    expect(findDraft([draft({ id: "42" })], "42")?.id).toBe("42");
  });

  it("is null for an unknown id", () => {
    expect(findDraft([draft({ id: 1 })], "2")).toBeNull();
  });
});

describe("draftPreviewUri", () => {
  it("prefers the medium variant", () => {
    expect(draftPreviewUri(draft())).toBe("m.jpg");
  });

  it("falls back to large, small and the original", () => {
    expect(draftPreviewUri(draft({ image: { thumb: null, small: "s.jpg", medium: null, large: "l.jpg", original: "o.jpg" } }))).toBe("l.jpg");
    expect(draftPreviewUri(draft({ image: { thumb: null, small: "s.jpg", medium: null, large: null, original: "o.jpg" } }))).toBe("s.jpg");
    expect(draftPreviewUri(draft({ image: { thumb: null, small: null, medium: null, large: null, original: "o.jpg" } }))).toBe("o.jpg");
  });
});

describe("draftThumbnailUri", () => {
  it("prefers the thumbnail, then small, medium and the original", () => {
    expect(draftThumbnailUri(draft())).toBe("t.jpg");
    expect(draftThumbnailUri(draft({ image: { thumb: null, small: "s.jpg", medium: "m.jpg", large: null, original: "o.jpg" } }))).toBe("s.jpg");
    expect(draftThumbnailUri(draft({ image: { thumb: null, small: null, medium: "m.jpg", large: null, original: "o.jpg" } }))).toBe("m.jpg");
    expect(draftThumbnailUri(draft({ image: { thumb: null, small: null, medium: null, large: null, original: "o.jpg" } }))).toBe("o.jpg");
  });
});

describe("formForDraft", () => {
  it("starts an empty form with the draft's topic when it is publishable", () => {
    expect(formForDraft(draft({ topic: "tanks" }))).toEqual({ title: "", description: "", topic: "tanks" });
  });

  it("uses the default topic for a topic users cannot publish under", () => {
    expect(formForDraft(draft({ topic: "unknown" })).topic).toBe("cichlids");
    expect(formForDraft(draft({ topic: "contest" })).topic).toBe("cichlids");
  });
});
