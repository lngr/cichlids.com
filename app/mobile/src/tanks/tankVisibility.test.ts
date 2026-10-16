import { describe, expect, it } from "vitest";
import { badgeFor, draftAfterProfileChange, draftAfterStreamChange, isDraftChanged, requiresConsentText } from "./tankVisibility";

describe("tank visibility draft", () => {
  it("keeps the stream within the profile when the profile becomes private", () => {
    expect(draftAfterProfileChange({ profile: "public", stream: "public" }, "private")).toEqual({ profile: "private", stream: "private" });
    expect(draftAfterProfileChange({ profile: "private", stream: "private" }, "public")).toEqual({ profile: "public", stream: "private" });
  });
  it("refuses a public stream below a private profile", () => {
    expect(draftAfterStreamChange({ profile: "private", stream: "private" }, "public")).toEqual({ profile: "private", stream: "private" });
    expect(draftAfterStreamChange({ profile: "public", stream: "public" }, "private")).toEqual({ profile: "public", stream: "private" });
  });
  it("tells a changed draft from the stored levels", () => {
    expect(isDraftChanged({ profile: "public", stream: "public" }, { profile: "public", stream: "public" })).toBe(false);
    expect(isDraftChanged({ profile: "public", stream: "public" }, { profile: "public", stream: "private" })).toBe(true);
  });
  it("asks for the consent text only when the profile becomes public", () => {
    expect(requiresConsentText({ profile: "private", stream: "private" }, { profile: "public", stream: "private" })).toBe(true);
    expect(requiresConsentText({ profile: "public", stream: "public" }, { profile: "public", stream: "private" })).toBe(false);
    expect(requiresConsentText({ profile: "public", stream: "public" }, { profile: "private", stream: "private" })).toBe(false);
  });
  it("names the badge an owner sees", () => {
    expect(badgeFor({ profile: "private", stream: "private" })).toBe("private");
    expect(badgeFor({ profile: "public", stream: "private" })).toBe("streamPrivate");
    expect(badgeFor({ profile: "public", stream: "public" })).toBeNull();
  });
});
