import type { StoryDetail, StoryItem, StorySubject } from "@cichlids/client-core";

export type SequencePage =
  | { kind: "item"; item: StoryItem; position: number }
  | { kind: "card"; subject: StorySubject; position: number };

/**
 * The pages a story is swiped through: its items and the subjects with a position, ordered by the
 * position they share. Subjects without a position are references and not part of the sequence.
 */
export function buildSequence(story: Pick<StoryDetail, "items" | "subjects">): SequencePage[] {
  const pages: SequencePage[] = [
    ...story.items.map((item): SequencePage => ({ kind: "item", item, position: Number(item.position) })),
    ...story.subjects.flatMap((subject): SequencePage[] =>
      subject.position === null ? [] : [{ kind: "card", subject, position: Number(subject.position) }]),
  ];
  return pages.sort((a, b) => a.position - b.position);
}

/**
 * The subjects shown with the story but not as a page: those without a position, in the order the
 * author added them.
 */
export function referencesOf(story: Pick<StoryDetail, "subjects">): StorySubject[] {
  return story.subjects.filter(subject => subject.position === null);
}
