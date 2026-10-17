import type { AuthorDto, CareKind, ReturnRetrospectiveDto, TankRegion } from "./generated/types.gen.js";
import { toTodayFollowed } from "./todaySections.js";
import type { TodayFollowed } from "./todaySections.js";
import type { RetrospectiveCard } from "./retrospective.js";

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

/**
 * The cards of the story presentation in order: a welcome card with the start of the absence, then the
 * cards that have data (overdue care, responses, followed tanks, region news) and, when any of them
 * exist, the next steps with the own tanks they refer to.
 */
export const returnCards = (retrospective: ReturnRetrospective): RetrospectiveCard[] => {
  const data: RetrospectiveCard[] = [];
  if (retrospective.overdueCare?.length) data.push({ kind: "return_overdue_care", items: retrospective.overdueCare });
  if (retrospective.responses) data.push({ kind: "return_responses", ...retrospective.responses });
  if (retrospective.followedTanks?.items.length) data.push({ kind: "return_followed_tanks", items: retrospective.followedTanks.items });
  const region = retrospective.regionNews;
  if (region && (region.tanks.length > 0 || region.stories.length > 0)) data.push({ kind: "return_region_news", tanks: region.tanks, stories: region.stories });
  if (data.length === 0) return [];
  const steps = retrospective.nextSteps;
  const closing: RetrospectiveCard[] = steps && (steps.careTanks.length > 0 || steps.timeLapseTanks.length > 0)
    ? [{ kind: "return_next_steps", careTanks: steps.careTanks, timeLapseTanks: steps.timeLapseTanks }]
    : [];
  return [{ kind: "return_welcome", since: retrospective.since }, ...data, ...closing];
};

/** Whether the app opens the retrospective on its own: it is due and not yet seen. */
export const opensOnStart = (retrospective: ReturnRetrospective | null): boolean => retrospective !== null && !retrospective.seen;
