import { describe, expect, it } from "vitest";
import type { StoryDetail, StoryItem, StorySubject } from "@cichlids/client-core";
import { buildSequence, referencesOf } from "./storySequence";

const item = (id: number, position: number, caption: string | null = null): StoryItem => ({
  id, mediaItemId: id * 10, position, caption, kind: "photo", image: { thumb: `t${id}`, small: null, medium: null, large: null }, video: null,
});
const subject = (id: number, type: StorySubject["type"], position: number | null): StorySubject => ({
  id, type, position, tank: type === "tank" ? { id, title: "Reef", category: null, image: null } : null, inhabitant: null,
  species: type === "species" ? { id, slug: null, genus: "G", name: "n", displayName: "G n" } : null,
});
const story = (items: StoryItem[], subjects: StorySubject[]) => ({ items, subjects }) as unknown as StoryDetail;

describe("story sequence", () => {
  it("orders items and positioned subjects by their shared position", () => {
    const pages = buildSequence(story(
      [item(1, 0, "a"), item(2, 2), item(3, 3)],
      [subject(7, "tank", 1), subject(8, "species", null)],
    ));

    expect(pages.map(page => (page.kind === "item" ? `item-${page.item.id}` : `card-${page.subject.id}`)))
      .toEqual(["item-1", "card-7", "item-2", "item-3"]);
  });

  it("keeps subjects without a position out of the sequence and returns them as references", () => {
    const detail = story([item(1, 0)], [subject(7, "tank", null), subject(8, "species", 1), subject(9, "species", null)]);

    expect(buildSequence(detail)).toHaveLength(2);
    expect(referencesOf(detail).map(s => s.id)).toEqual([7, 9]);
  });

  it("is the single item of a single-photo story", () => {
    const pages = buildSequence(story([item(1, 0)], []));

    expect(pages).toHaveLength(1);
    expect(pages[0]).toMatchObject({ kind: "item", item: { id: 1 } });
  });
});
