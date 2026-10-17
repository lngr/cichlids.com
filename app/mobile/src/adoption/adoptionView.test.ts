import { describe, expect, it } from "vitest";
import { adoptionProblemKey, suggestionDate, adoptionRoute, choicePosition, gridColumns, gridImageUrl } from "./adoptionView";

describe("adoptionRoute", () => {
  it("opens the adoption below the tank", () => {
    expect(adoptionRoute(5)).toBe("/tanks/5/adopt");
  });
});

describe("choicePosition", () => {
  it("counts the chosen photos from one in the order of the choice", () => {
    expect(choicePosition([9, 4, 7], 9)).toBe(1);
    expect(choicePosition([9, 4, 7], 7)).toBe(3);
  });

  it("has no position for a photo that is not chosen", () => {
    expect(choicePosition([9, 4], 5)).toBeNull();
    expect(choicePosition([], 5)).toBeNull();
  });
});

describe("gridImageUrl", () => {
  it("prefers the small size and falls back to the other sizes", () => {
    expect(gridImageUrl({ thumb: "t", small: "s", medium: "m", large: "l" })).toBe("s");
    expect(gridImageUrl({ thumb: "t", small: null, medium: "m", large: null })).toBe("t");
    expect(gridImageUrl({ thumb: null, small: null, medium: null, large: "l" })).toBe("l");
  });

  it("has no URL without an image", () => {
    expect(gridImageUrl(null)).toBeUndefined();
    expect(gridImageUrl({ thumb: null, small: null, medium: null, large: null })).toBeUndefined();
  });
});

describe("adoptionProblemKey", () => {
  it("names a message for every refusal", () => {
    expect(adoptionProblemKey("already_adopted")).toBe("adoption.alreadyAdopted");
    expect(adoptionProblemKey("tank_not_found")).toBe("adoption.notFound");
    expect(adoptionProblemKey("photo_not_found")).toBe("adoption.photoNotFound");
    expect(adoptionProblemKey("invalid")).toBe("adoption.invalid");
  });
});

describe("gridColumns", () => {
  it("shows at least three columns and widens with the screen", () => {
    expect(gridColumns(320)).toBe(3);
    expect(gridColumns(390)).toBe(4);
    expect(gridColumns(880)).toBe(9);
  });
});

describe("suggestionDate", () => {
  it("reads the day of a capture time as the camera recorded it, in any time zone", () => {
    expect(suggestionDate({ date: "2010-05-01T23:30:00+00:00", takenAt: "2010-05-01T23:30:00+00:00" }, "en")).toBe("May 1, 2010");
    expect(suggestionDate({ date: "2010-05-01T23:30:00+00:00", takenAt: "2010-05-01T23:30:00+00:00" }, "de")).toBe("1. Mai 2010");
  });

  it("reads the written day of a capture time that carries an offset", () => {
    expect(suggestionDate({ date: "2010-05-01T21:30:00+00:00", takenAt: "2010-05-02T00:30:00+03:00" }, "en")).toBe("May 2, 2010");
  });

  it("has no label for an unreadable date", () => {
    expect(suggestionDate({ date: "x", takenAt: null }, "en")).toBe("");
  });
});
