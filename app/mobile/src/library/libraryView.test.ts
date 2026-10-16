import { describe, expect, it } from "vitest";
import type { LibraryItem, LibraryPlace } from "@cichlids/client-core";
import { describePlace, formatClock, listParams, previewImage, tileImage, tileLabel } from "./libraryView";

const t = (key: string, options: Record<string, unknown> = {}) =>
  `${key}${Object.keys(options).length ? ` ${JSON.stringify(options)}` : ""}`;

const place = (patch: Partial<LibraryPlace>): LibraryPlace => ({
  kind: "tank", name: null, tankId: null, careEntryId: null, storyId: null, storySlug: null, section: null, careKind: null, at: null, ...patch,
});
const item = (patch: Partial<LibraryItem>): LibraryItem => ({
  id: 1, kind: "photo", createdAt: "2026-10-16T10:00:00Z", width: 800, height: 600, video: null, placeCount: 0, placeKinds: [],
  image: { thumb: "t", small: "s", medium: "m", large: "l" }, ...patch,
});

describe("library filters", () => {
  it("leaves out the parameters of the all filters", () => {
    expect(listParams("all", "all")).toEqual({});
    expect(listParams("photo", "all")).toEqual({ kind: "photo" });
    expect(listParams("all", "unlinked")).toEqual({ linked: false });
    expect(listParams("video", "linked")).toEqual({ kind: "video", linked: true });
  });
});

describe("describePlace", () => {
  it("names the tank section and the tank of a tank place", () => {
    const line = describePlace(place({ name: "Reef", section: "time_lapse" }), t, "en");
    expect(line).toContain("library.placeNamed");
    expect(line).toContain("library.section.time_lapse");
    expect(line).toContain('"name":"Reef"');
  });
  it("shows only the kind for a place of another member", () => {
    expect(describePlace(place({ kind: "story" }), t, "en")).toBe("library.place.story");
  });
  it("adds the date of a care entry", () => {
    const line = describePlace(place({ kind: "care_entry", name: "Reef", at: "2026-10-01T10:00:00Z" }), t, "en");
    expect(line).toContain('"name":"Reef, ');
    expect(line).toContain("2026");
  });
});

describe("tiles", () => {
  it("reads a different label for a file without a place", () => {
    expect(tileLabel(item({ placeCount: 0 }), t, "en")).toContain("library.tileUnused");
    expect(tileLabel(item({ placeCount: 2 }), t, "en")).toContain("library.tileUsed");
    expect(tileLabel(item({ placeCount: 2 }), t, "en")).toContain('"places":2');
  });
  it("shows the small photo, the poster of a video and nothing for a video that is not ready", () => {
    expect(tileImage(item({}))).toBe("s");
    expect(tileImage(item({ kind: "video", image: null, video: { posterUrl: "p" } as never }))).toBe("p");
    expect(tileImage(item({ kind: "video", image: null, video: { posterUrl: null } as never }))).toBeNull();
    expect(previewImage(item({}))).toBe("l");
  });
  it("formats a clock", () => {
    expect(formatClock(5)).toBe("0:05");
    expect(formatClock(65.4)).toBe("1:05");
  });
});
