import { assign, setup } from "xstate";
import type { ArchiveRetrospectiveDto, ImageUrlsDto } from "./generated/types.gen.js";

/** The image of a card in the sizes the API derives; a size is null when it does not exist. */
export type RetrospectiveImage = ImageUrlsDto;

/**
 * One card of a retrospective. The kinds are shared by every retrospective, so one presentation
 * shows the cards of each of them.
 */
export type RetrospectiveCard =
  | { kind: "member_since"; year: number; month: number }
  | { kind: "stats"; photos: number; comments: number; stars: number }
  | { kind: "most_viewed"; storyId: number; slug: string; title: string | null; viewCount: number; image: RetrospectiveImage | null }
  | { kind: "archive_tank"; tankId: number; title: string; image: RetrospectiveImage | null }
  | { kind: "tank_today" };

export type Retrospective = { cards: RetrospectiveCard[] };

/**
 * The cards of an archive retrospective in presentation order: member since, the numbers, the most
 * viewed story, one card per archive tank and the closing question how the tank looks today. A card
 * without data is left out.
 */
export function toArchiveRetrospective(dto: ArchiveRetrospectiveDto): Retrospective {
  const cards: RetrospectiveCard[] = [];
  if (dto.memberSince) cards.push({ kind: "member_since", year: Number(dto.memberSince.year), month: Number(dto.memberSince.month) });
  if (dto.stats) {
    const stats = { photos: Number(dto.stats.photos), comments: Number(dto.stats.comments), stars: Number(dto.stats.stars) };
    if (stats.photos + stats.comments + stats.stars > 0) cards.push({ kind: "stats", ...stats });
  }
  if (dto.mostViewed) {
    const { storyId, slug, title, viewCount, image } = dto.mostViewed;
    cards.push({ kind: "most_viewed", storyId: Number(storyId), slug, title, viewCount: Number(viewCount), image });
  }
  for (const tank of dto.tanks) cards.push({ kind: "archive_tank", tankId: Number(tank.id), title: tank.title, image: tank.image });
  cards.push({ kind: "tank_today" });
  return { cards };
}

/** The figures of a stats card that have a value, in the order they are shown. */
export function statEntries(card: Extract<RetrospectiveCard, { kind: "stats" }>): { key: "photos" | "comments" | "stars"; value: number }[] {
  return (["photos", "comments", "stars"] as const).flatMap(key => card[key] > 0 ? [{ key, value: card[key] }] : []);
}

/** How long a card stays before the player moves on, in milliseconds. */
export const RETROSPECTIVE_CARD_MS = 6000;

/**
 * The player of a retrospective: the index of the card on display and whether the cards advance on
 * their own. The last card stays on display; the player pauses when playback reaches it.
 */
export const retrospectivePlayerMachine = setup({
  types: {
    context: {} as { count: number; index: number },
    input: {} as { count: number },
    events: {} as { type: "NEXT" | "PREV" | "PLAY" | "PAUSE" | "TICK" } | { type: "SEEK"; index: number },
  },
  guards: {
    last: ({ context }) => context.index >= context.count - 1,
  },
  actions: {
    next: assign({ index: ({ context }) => Math.max(0, Math.min(context.count - 1, context.index + 1)) }),
    previous: assign({ index: ({ context }) => Math.max(0, context.index - 1) }),
    seek: assign({ index: ({ context, event }) => event.type === "SEEK" ? Math.max(0, Math.min(context.count - 1, Math.round(event.index))) : context.index }),
    restart: assign({ index: ({ context }) => context.index >= context.count - 1 ? 0 : context.index }),
  },
}).createMachine({
  id: "retrospectivePlayer",
  context: ({ input }) => ({ count: input.count, index: 0 }),
  initial: "playing",
  on: {
    NEXT: { actions: "next" },
    PREV: { actions: "previous" },
    SEEK: { actions: "seek" },
  },
  states: {
    playing: {
      on: {
        PAUSE: "paused",
        TICK: [{ guard: "last", target: "paused" }, { actions: "next" }],
      },
    },
    paused: { on: { PLAY: { target: "playing", actions: "restart" } } },
  },
});
