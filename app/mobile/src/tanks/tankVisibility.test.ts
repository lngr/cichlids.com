import { describe, expect, it } from "vitest";
import { badgeFor, draftAfterProfileChange, draftAfterStreamChange, isDraftChanged, isWider, LEVELS, requiresConsentText } from "./tankVisibility";

describe("tank visibility draft", () => {
  it("orders the levels from public to private", () => {
    expect(LEVELS).toEqual(["public", "followers", "selected", "private"]);
    expect(isWider("public", "followers")).toBe(true);
    expect(isWider("followers", "selected")).toBe(true);
    expect(isWider("selected", "private")).toBe(true);
    expect(isWider("selected", "followers")).toBe(false);
    expect(isWider("followers", "followers")).toBe(false);
  });
  it("narrows the stream to the profile when the profile becomes narrower", () => {
    expect(draftAfterProfileChange({ profile: "public", stream: "public" }, "private")).toEqual({ profile: "private", stream: "private" });
    expect(draftAfterProfileChange({ profile: "public", stream: "public" }, "followers")).toEqual({ profile: "followers", stream: "followers" });
    expect(draftAfterProfileChange({ profile: "public", stream: "followers" }, "selected")).toEqual({ profile: "selected", stream: "selected" });
    expect(draftAfterProfileChange({ profile: "public", stream: "selected" }, "followers")).toEqual({ profile: "followers", stream: "selected" });
  });
  it("keeps the stream when the profile becomes wider", () => {
    expect(draftAfterProfileChange({ profile: "private", stream: "private" }, "public")).toEqual({ profile: "public", stream: "private" });
    expect(draftAfterProfileChange({ profile: "selected", stream: "selected" }, "followers")).toEqual({ profile: "followers", stream: "selected" });
  });
  it("refuses a stream wider than the profile", () => {
    expect(draftAfterStreamChange({ profile: "private", stream: "private" }, "public")).toEqual({ profile: "private", stream: "private" });
    expect(draftAfterStreamChange({ profile: "selected", stream: "selected" }, "followers")).toEqual({ profile: "selected", stream: "selected" });
    expect(draftAfterStreamChange({ profile: "followers", stream: "followers" }, "selected")).toEqual({ profile: "followers", stream: "selected" });
    expect(draftAfterStreamChange({ profile: "public", stream: "public" }, "private")).toEqual({ profile: "public", stream: "private" });
  });
  it("tells a changed draft from the stored levels", () => {
    expect(isDraftChanged({ profile: "public", stream: "public" }, { profile: "public", stream: "public" })).toBe(false);
    expect(isDraftChanged({ profile: "public", stream: "public" }, { profile: "public", stream: "followers" })).toBe(true);
  });
  it("asks for the consent text only when the profile becomes public", () => {
    expect(requiresConsentText({ profile: "private", stream: "private" }, { profile: "public", stream: "private" })).toBe(true);
    expect(requiresConsentText({ profile: "followers", stream: "followers" }, { profile: "public", stream: "followers" })).toBe(true);
    expect(requiresConsentText({ profile: "public", stream: "public" }, { profile: "public", stream: "private" })).toBe(false);
    expect(requiresConsentText({ profile: "public", stream: "public" }, { profile: "followers", stream: "followers" })).toBe(false);
  });
  it("names the badge an owner sees", () => {
    expect(badgeFor({ profile: "private", stream: "private" })).toBe("private");
    expect(badgeFor({ profile: "selected", stream: "private" })).toBe("selected");
    expect(badgeFor({ profile: "followers", stream: "selected" })).toBe("followers");
    expect(badgeFor({ profile: "public", stream: "private" })).toBe("streamPrivate");
    expect(badgeFor({ profile: "public", stream: "selected" })).toBe("streamSelected");
    expect(badgeFor({ profile: "public", stream: "followers" })).toBe("streamFollowers");
    expect(badgeFor({ profile: "public", stream: "public" })).toBeNull();
  });
});
