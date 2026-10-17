import type {
  AuthorDto, DimensionUnit, FishDto, FishTankDto, FishTimelineItemDto, FishTimelineKind, ImageUrlsDto, VideoDto,
} from "./generated/types.gen.js";
import type { RemovalReason, StockedSpecies } from "./stocking.js";

/** The longest length of a fish in centimetres the API accepts. */
export const FISH_MAX_LENGTH_CM = 300;
/** The highest generation of the offspring of a spawning. */
export const FISH_MAX_GENERATION = 99;
const CENTIMETERS_PER_INCH = 2.54;

export type FishLengthUnit = DimensionUnit;
export const FISH_LENGTH_UNITS: readonly FishLengthUnit[] = ["cm", "inch"];
export type FishTimelineItemKind = FishTimelineKind;

/** A tank a fish lives or lived in; deleted is true for a tank its owner deleted. */
export type FishTank = { id: number; title: string; deleted: boolean };

/** A photo on the page of a fish. */
export type FishPhoto = { mediaItemId: number; width: number | null; height: number | null; image: ImageUrlsDto; createdAt: string };

/** A measured length: centimetres, with the value and unit the owner typed. */
export type FishGrowth = { lengthCm: number; inputValue: number; inputUnit: FishLengthUnit };

/** A spawning; generation is the generation of a pure-bred offspring (1 for F1) and null for a hybrid. */
export type FishSpawning = { generation: number | null; hybrid: boolean };

/** A move between tanks of the owner; a tank is null when the viewer may not see it. */
export type FishMove = { fromTank: FishTank | null; toTank: FishTank | null };

/**
 * One item of the timeline, dated by on (YYYY-MM-DD). eventId names the stored event of a growth, spawning,
 * move or death. The member that matches the kind is set: growth, spawning and move for their kinds, year for an
 * anniversary and reason for a fish that was given away or moved.
 */
export type FishTimelineItem = {
  kind: FishTimelineItemKind;
  on: string;
  eventId: number | null;
  growth: FishGrowth | null;
  spawning: FishSpawning | null;
  move: FishMove | null;
  year: number | null;
  reason: RemovalReason | null;
};

/**
 * The page of a named fish: photos, videos and timeline newest first. A fish that died is a memorial;
 * isOwner is true for the member the fish belongs to.
 */
export type FishPage = {
  id: number;
  name: string;
  species: StockedSpecies | null;
  owner: AuthorDto;
  isOwner: boolean;
  tank: FishTank;
  movedInOn: string | null;
  removedOn: string | null;
  removalReason: RemovalReason | null;
  memorial: boolean;
  photos: FishPhoto[];
  videos: VideoDto[];
  timeline: FishTimelineItem[];
};

const toTank = (dto: FishTankDto | null): FishTank | null => (dto ? { id: Number(dto.id), title: dto.title, deleted: dto.deleted } : null);

const toTimelineItem = (dto: FishTimelineItemDto): FishTimelineItem => ({
  kind: dto.kind,
  on: dto.on,
  eventId: dto.eventId === null ? null : Number(dto.eventId),
  growth: dto.growth ? { lengthCm: Number(dto.growth.lengthCm), inputValue: Number(dto.growth.inputValue), inputUnit: dto.growth.inputUnit } : null,
  spawning: dto.spawning ? { generation: dto.spawning.generation === null ? null : Number(dto.spawning.generation), hybrid: dto.spawning.hybrid } : null,
  move: dto.move ? { fromTank: toTank(dto.move.fromTank), toTank: toTank(dto.move.toTank) } : null,
  year: dto.year === null ? null : Number(dto.year),
  reason: dto.reason,
});

export const toFishPage = (dto: FishDto): FishPage => ({
  id: Number(dto.id),
  name: dto.name,
  species: dto.species ? { ...dto.species, id: Number(dto.species.id) } : null,
  owner: dto.owner,
  isOwner: dto.isOwner,
  tank: toTank(dto.tank)!,
  movedInOn: dto.movedInOn,
  removedOn: dto.removedOn,
  removalReason: dto.removalReason,
  memorial: dto.memorial,
  photos: dto.photos.map(photo => ({
    mediaItemId: Number(photo.mediaItemId),
    width: photo.width === null ? null : Number(photo.width),
    height: photo.height === null ? null : Number(photo.height),
    image: photo.image,
    createdAt: photo.createdAt,
  })),
  videos: dto.videos,
  timeline: dto.timeline.map(toTimelineItem),
});

/** The request of a growth entry; on defaults to the local date. */
export type GrowthInput = { length: number; unit: FishLengthUnit; on?: string | null };
/** The request of a spawning: a generation for a pure-bred offspring, or hybrid. */
export type SpawningInput = { generation?: number | null; hybrid?: boolean; on?: string | null };

/** The values of the growth form: the length as the text typed and its unit. */
export type GrowthDraft = { length: string; unit: FishLengthUnit };
export type GrowthProblem = "length" | "length_too_large";

/** Parses a decimal typed with a point or a comma; NaN when the text is no number. */
export const parseLength = (text: string): number => {
  const trimmed = text.trim().replace(",", ".");
  return /^\d+(\.\d+)?$/.test(trimmed) ? Number(trimmed) : Number.NaN;
};

/** The length in centimetres of a value in the given unit. */
export const lengthInCentimeters = (value: number, unit: FishLengthUnit): number => (unit === "inch" ? value * CENTIMETERS_PER_INCH : value);

/** The first problem of a growth form, or null: the length is a positive number of at most 300 cm. */
export function checkGrowthDraft(draft: GrowthDraft): GrowthProblem | null {
  const value = parseLength(draft.length);
  if (!Number.isFinite(value) || value <= 0) return "length";
  return lengthInCentimeters(value, draft.unit) > FISH_MAX_LENGTH_CM ? "length_too_large" : null;
}

/** The request body of a form that passed checkGrowthDraft; an empty day is left out. */
export const growthInput = (draft: GrowthDraft, on = ""): GrowthInput => {
  const input: GrowthInput = { length: parseLength(draft.length), unit: draft.unit };
  return on.trim() === "" ? input : { ...input, on: on.trim() };
};

/** The values of the spawning form: a pure-bred offspring with the generation typed, or a hybrid. */
export type SpawningDraft = { hybrid: boolean; generation: string };
export type SpawningProblem = "generation";

/** The first problem of a spawning form, or null: a pure-bred offspring needs a generation from 1 to 99. */
export function checkSpawningDraft(draft: SpawningDraft): SpawningProblem | null {
  if (draft.hybrid) return null;
  const generation = Number(draft.generation.trim());
  return draft.generation.trim() !== "" && Number.isInteger(generation) && generation >= 1 && generation <= FISH_MAX_GENERATION ? null : "generation";
}

/** The request body of a form that passed checkSpawningDraft. */
export const spawningInput = (draft: SpawningDraft, on = ""): SpawningInput => {
  const input: SpawningInput = draft.hybrid ? { hybrid: true } : { generation: Number(draft.generation.trim()) };
  return on.trim() === "" ? input : { ...input, on: on.trim() };
};

/** The newest growth entry of a page, or null. */
export const latestGrowth = (page: FishPage): FishGrowth | null =>
  page.timeline.find(item => item.kind === "growth")?.growth ?? null;

/** The tanks of the owner a fish can move to: every live tank except the one it lives in. */
export const moveTargets = <T extends { id: number }>(tanks: readonly T[], current: number): T[] => tanks.filter(tank => tank.id !== current);
