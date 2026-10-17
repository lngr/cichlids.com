import type { FollowedTankDto, FollowedUpdateDto, MilestoneDto, TodayFollowedDto } from "./generated/types.gen.js";

export type FollowedUpdateKind = FollowedUpdateDto["kind"];
export type MilestoneKind = MilestoneDto["kind"];

/** The images of a photo update, as the API returns them. */
export type FollowedImage = NonNullable<FollowedUpdateDto["image"]>;

/** The story of an update, as far as it is needed to open it. */
export type FollowedStory = { id: number; slug: string | null; title: string | null };

/** The newest update of a followed tank the member may see; at is an ISO instant. */
export type FollowedUpdate = {
  kind: FollowedUpdateKind;
  at: string;
  careKind: FollowedUpdateDto["careKind"];
  story: FollowedStory | null;
  image: FollowedImage | null;
};

/** A followed tank with its owner and its newest visible update; update is null for a tank without one. */
export type FollowedTank = {
  tankId: number;
  title: string;
  owner: FollowedTankDto["owner"];
  update: FollowedUpdate | null;
};

/** The followed tanks with an update in the last 30 days, at most five; hasMore tells whether more tanks have one. */
export type TodayFollowed = { items: FollowedTank[]; hasMore: boolean };

/** A milestone of an own tank; value is the streak length in weeks, the tank year or 0 for the first care week. */
export type Milestone = { key: string; kind: MilestoneKind; tankId: number; tankTitle: string; value: number; reachedOn: string };

const toUpdate = (dto: FollowedUpdateDto): FollowedUpdate => ({
  kind: dto.kind,
  at: dto.at,
  careKind: dto.careKind,
  story: dto.story ? { id: Number(dto.story.id), slug: dto.story.slug, title: dto.story.title } : null,
  image: dto.image,
});

export const toFollowedTank = (dto: FollowedTankDto): FollowedTank => ({
  tankId: Number(dto.tankId),
  title: dto.title,
  owner: dto.owner,
  update: dto.update ? toUpdate(dto.update) : null,
});

export const toTodayFollowed = (dto: TodayFollowedDto): TodayFollowed => ({ items: dto.items.map(toFollowedTank), hasMore: dto.hasMore });

export const toMilestone = (dto: MilestoneDto): Milestone => ({
  key: dto.key, kind: dto.kind, tankId: Number(dto.tankId), tankTitle: dto.tankTitle, value: Number(dto.value), reachedOn: dto.reachedOn,
});

const DAY_MS = 86_400_000;

/** The whole days from an ISO instant to now, never negative. */
export const daysSince = (instant: string, now: Date): number => Math.max(0, Math.floor((now.getTime() - new Date(instant).getTime()) / DAY_MS));

/** The image of a photo update that fits a card: the medium size, else the next smaller or larger one. */
export const updateImageUri = (update: FollowedUpdate): string | null =>
  update.image?.medium ?? update.image?.small ?? update.image?.large ?? update.image?.thumb ?? null;

/** The path of the page that shows an update: the story, otherwise the tank. */
export const updatePath = (tank: FollowedTank): string =>
  tank.update?.story ? `/gallery/${tank.update.story.slug ?? tank.update.story.id}` : `/tanks/${tank.tankId}`;
