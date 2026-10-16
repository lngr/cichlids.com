import type {
  ImageUrlsDto, MediaKind, MyStoryDto, StoryEditorDto, StoryItemDto, StorySubjectDto, StorySubjectType, StoryState, StoryTopic, VideoDto,
} from "./generated/types.gen.js";

export const STORY_CAPTION_MAX_LENGTH = 500;
export const STORY_TITLE_MAX_LENGTH = 200;
export const STORY_DESCRIPTION_MAX_LENGTH = 5000;

/** What can be added to a story: the topics a member may choose. */
export const STORY_TOPICS = ["cichlids", "tanks", "offtopic"] as const;
export type PublishableStoryTopic = (typeof STORY_TOPICS)[number];

/** An item of a story: a link to a file with the caption of this story only. The id is the id of the link. */
export type EditorItem = {
  id: number;
  mediaItemId: number;
  position: number;
  caption: string | null;
  kind: MediaKind;
  image: ImageUrlsDto | null;
  video: VideoDto | null;
};

/** A subject of a story: exactly the card of its type is set. A position makes it a card in the sequence. */
export type EditorSubject = {
  id: number;
  type: StorySubjectType;
  position: number | null;
  tank: { id: number; title: string; image: ImageUrlsDto | null } | null;
  inhabitant: { id: number; tankId: number; tankTitle: string; speciesName: string | null; count: number | null } | null;
  species: { id: number; displayName: string } | null;
};

/** A story of the signed-in member as the editor shows it. */
export type StoryEditor = {
  id: number;
  state: StoryState;
  slug: string | null;
  title: string | null;
  description: string | null;
  topic: StoryTopic;
  coverItemId: number | null;
  cover: ImageUrlsDto | null;
  items: EditorItem[];
  subjects: EditorSubject[];
  unavailableSubjectIds: number[];
  quickShareMediaItemId: number | null;
  updatedAt: string;
};

/** A story of the signed-in member in the list of their stories. */
export type MyStory = {
  id: number;
  state: StoryState;
  slug: string | null;
  title: string | null;
  topic: StoryTopic;
  itemCount: number;
  cover: ImageUrlsDto | null;
  updatedAt: string;
};

/** One entry of the sequence of a story. */
export type EditorEntry = { kind: "item" | "subject"; id: number };

export type StoryTextRequest = { title: string | null; description: string | null; topic: PublishableStoryTopic | null };
export type StorySubjectRequest = { type: StorySubjectType; id: number; positioned: boolean };

const toItem = (item: StoryItemDto): EditorItem => ({
  id: Number(item.id), mediaItemId: Number(item.mediaItemId), position: Number(item.position), caption: item.caption,
  kind: item.kind, image: item.image, video: item.video,
});

const toSubject = (subject: StorySubjectDto): EditorSubject => ({
  id: Number(subject.id),
  type: subject.type,
  position: subject.position === null ? null : Number(subject.position),
  tank: subject.tank && { id: Number(subject.tank.id), title: subject.tank.title, image: subject.tank.image },
  inhabitant: subject.inhabitant && {
    id: Number(subject.inhabitant.id), tankId: Number(subject.inhabitant.tankId), tankTitle: subject.inhabitant.tankTitle,
    speciesName: subject.inhabitant.species?.displayName ?? null, count: subject.inhabitant.count === null ? null : Number(subject.inhabitant.count),
  },
  species: subject.species && { id: Number(subject.species.id), displayName: subject.species.displayName },
});

export const toStoryEditor = (story: StoryEditorDto): StoryEditor => ({
  id: Number(story.id),
  state: story.state,
  slug: story.slug,
  title: story.title,
  description: story.description,
  topic: story.topic,
  coverItemId: story.coverItemId === null ? null : Number(story.coverItemId),
  cover: story.cover,
  items: story.items.map(toItem),
  subjects: story.subjects.map(toSubject),
  unavailableSubjectIds: story.unavailableSubjectIds.map(Number),
  quickShareMediaItemId: story.quickShareMediaItemId === null ? null : Number(story.quickShareMediaItemId),
  updatedAt: story.updatedAt,
});

export const toMyStory = (story: MyStoryDto): MyStory => ({
  id: Number(story.id), state: story.state, slug: story.slug, title: story.title, topic: story.topic,
  itemCount: Number(story.itemCount), cover: story.cover, updatedAt: story.updatedAt,
});

/**
 * The sequence of a story: its items and its positioned subjects ordered by position, an item before a
 * subject at the same position.
 */
export function storySequence(story: Pick<StoryEditor, "items" | "subjects">): EditorEntry[] {
  const entries = [
    ...story.items.map(item => ({ kind: "item" as const, id: item.id, position: item.position, rank: 0 })),
    ...story.subjects.filter(subject => subject.position !== null).map(subject => ({ kind: "subject" as const, id: subject.id, position: subject.position!, rank: 1 })),
  ];
  return entries.sort((a, b) => a.position - b.position || a.rank - b.rank || a.id - b.id).map(({ kind, id }) => ({ kind, id }));
}

/**
 * The sequence after the entry at the index moved by the offset (-1 up, 1 down), or the same sequence when
 * the move would leave it.
 */
export function moveEntry(order: readonly EditorEntry[], index: number, offset: -1 | 1): EditorEntry[] {
  const target = index + offset;
  if (index < 0 || index >= order.length || target < 0 || target >= order.length) return [...order];
  const next = [...order];
  [next[index], next[target]] = [next[target]!, next[index]!];
  return next;
}

/** Whether a caption fits the limit; blank counts as no caption. */
export const isValidCaption = (caption: string): boolean => caption.trim().length <= STORY_CAPTION_MAX_LENGTH;

/** The caption to send: trimmed, null when blank. */
export const normalizeCaption = (caption: string): string | null => (caption.trim().length === 0 ? null : caption.trim());

/** Whether a story can be published as it is: a title within the limit and at least one item. */
export function canPublishStory(story: Pick<StoryEditor, "items">, title: string): boolean {
  const trimmed = title.trim();
  return trimmed.length > 0 && trimmed.length <= STORY_TITLE_MAX_LENGTH && story.items.length > 0;
}
