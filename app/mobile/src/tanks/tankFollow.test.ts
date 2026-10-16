import { describe, expect, it } from "vitest";
import { followControlOf, undoesFollow } from "./tankFollow";

const none = { state: "none", canFollow: true } as const;

describe("follow control of a tank", () => {
  it("shows nothing to the owner and without follow information", () => {
    expect(followControlOf(none, "public", true, true)).toBe("hidden");
    expect(followControlOf(null, "public", false, true)).toBe("hidden");
    expect(followControlOf(undefined, "public", false, true)).toBe("hidden");
  });
  it("asks anonymous visitors of a public tank to sign in", () => {
    expect(followControlOf({ state: "none", canFollow: false }, "public", false, false)).toBe("login");
    expect(followControlOf({ state: "none", canFollow: false }, "followers", false, false)).toBe("hidden");
  });
  it("follows a public tank at once and asks to follow a tank for followers", () => {
    expect(followControlOf(none, "public", false, true)).toBe("follow");
    expect(followControlOf(none, "followers", false, true)).toBe("request");
  });
  it("shows an open request and an approved follow", () => {
    expect(followControlOf({ state: "requested", canFollow: false }, "followers", false, true)).toBe("requested");
    expect(followControlOf({ state: "following", canFollow: false }, "public", false, true)).toBe("following");
  });
  it("hides the control when the tank does not take followers from the member", () => {
    expect(followControlOf({ state: "none", canFollow: false }, "selected", false, true)).toBe("hidden");
  });
  it("withdraws or stops on a second press", () => {
    expect(undoesFollow("requested")).toBe(true);
    expect(undoesFollow("following")).toBe(true);
    expect(undoesFollow("follow")).toBe(false);
    expect(undoesFollow("request")).toBe(false);
  });
});
