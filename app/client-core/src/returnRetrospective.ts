import type { AuthorDto, CareKind, ReturnRetrospectiveDto, TankRegion } from "./generated/types.gen.js";
import { toTodayFollowed } from "./todaySections.js";
import type { TodayFollowed } from "./todaySections.js";

type Author = AuthorDto;

/** A care rule of an own tank whose due date has passed. */
export type ReturnOverdueItem = { tankId: number; tankTitle: string; kind: CareKind; daysOverdue: number };

/** What other members did with the content of the member since the absence began. */
export type ReturnResponses = { kudos: number; comments: number };

/** A new public tank in a region the member is interested in. */
export type ReturnRegionTank = { tankId: number; title: string; region: TankRegion | null; owner: Author };

/** A new published story about public tanks in a region the member is interested in. */
export type ReturnRegionStory = { id: number; slug: string | null; title: string | null; author: Author };

/** An own tank a next step refers to. */
export type ReturnTankRef = { tankId: number; title: string };

/** The own tanks with overdue care and the own tanks with a time-lapse. */
export type ReturnNextSteps = { careTanks: ReturnTankRef[]; timeLapseTanks: ReturnTankRef[] };

/**
 * The return retrospective of a member. since is the ISO instant the absence began. seen tells whether
 * the member already saw it. A card without data is null.
 */
export type ReturnRetrospective = {
  seen: boolean;
  since: string;
  overdueCare: ReturnOverdueItem[] | null;
  responses: ReturnResponses | null;
  followedTanks: TodayFollowed | null;
  regionNews: { tanks: ReturnRegionTank[]; stories: ReturnRegionStory[] } | null;
  nextSteps: ReturnNextSteps | null;
};

/** The cards of the story presentation, in the order they appear. */
export type ReturnCardKind = "overdue_care" | "responses" | "followed_tanks" | "region_news" | "next_steps";

const toRef = (tank: { tankId: number | string; title: string }): ReturnTankRef => ({ tankId: Number(tank.tankId), title: tank.title });

export const toReturnRetrospective = (dto: ReturnRetrospectiveDto): ReturnRetrospective => ({
  seen: dto.seen,
  since: dto.since,
  overdueCare: dto.overdueCare
    ? dto.overdueCare.items.map(item => ({
        tankId: Number(item.tankId), tankTitle: item.tankTitle, kind: item.kind, daysOverdue: Number(item.daysOverdue),
      }))
    : null,
  responses: dto.responses ? { kudos: Number(dto.responses.kudos), comments: Number(dto.responses.comments) } : null,
  followedTanks: dto.followedTanks ? toTodayFollowed(dto.followedTanks) : null,
  regionNews: dto.regionNews
    ? {
        tanks: dto.regionNews.tanks.map(tank => ({ tankId: Number(tank.tankId), title: tank.title, region: tank.region, owner: tank.owner })),
        stories: dto.regionNews.stories.map(story => ({ id: Number(story.id), slug: story.slug, title: story.title, author: story.author })),
      }
    : null,
  nextSteps: dto.nextSteps
    ? { careTanks: dto.nextSteps.careTanks.map(toRef), timeLapseTanks: dto.nextSteps.timeLapseTanks.map(toRef) }
    : null,
});

/** The cards that have data, in presentation order; the next steps close the story when there are any. */
export const returnCards = (retrospective: ReturnRetrospective): ReturnCardKind[] => {
  const cards: ReturnCardKind[] = [];
  if (retrospective.overdueCare?.length) cards.push("overdue_care");
  if (retrospective.responses) cards.push("responses");
  if (retrospective.followedTanks?.items.length) cards.push("followed_tanks");
  if (retrospective.regionNews && (retrospective.regionNews.tanks.length > 0 || retrospective.regionNews.stories.length > 0)) cards.push("region_news");
  const steps = retrospective.nextSteps;
  if (cards.length > 0 && steps && (steps.careTanks.length > 0 || steps.timeLapseTanks.length > 0)) cards.push("next_steps");
  return cards;
};

/** Whether the app opens the retrospective on its own: it is due and not yet seen. */
export const opensOnStart = (retrospective: ReturnRetrospective | null): boolean => retrospective !== null && !retrospective.seen;
