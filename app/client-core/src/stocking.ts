import type { InhabitantDto, InhabitantListDto, InhabitantRemovalReason } from "./generated/types.gen.js";

/** The longest name of a named fish, as the API accepts it. */
export const INHABITANT_NAME_MAX_LENGTH = 80;
/** The largest group the API accepts. */
export const INHABITANT_MAX_COUNT = 10000;

export type RemovalReason = InhabitantRemovalReason;
export const REMOVAL_REASONS: readonly RemovalReason[] = ["died", "given_away", "moved"];

/** The species of an inhabitant as the catalog names it. */
export type StockedSpecies = { id: number; slug: string | null; genus: string; name: string; displayName: string };

/**
 * An inhabitant of a tank: a group (species and count) or a named fish (name, count one, optional
 * species). The days are YYYY-MM-DD in the local dates of the owner. A migrated inhabitant has no
 * movedInOn and counts as present since the tank was created.
 */
export type StockedInhabitant = {
  id: number;
  species: StockedSpecies | null;
  count: number | null;
  name: string | null;
  movedInOn: string | null;
  removedOn: string | null;
  removalReason: RemovalReason | null;
};

/** The stocking of a tank: who lives in it, named fish first, and the history of those who left. */
export type Stocking = { current: StockedInhabitant[]; history: StockedInhabitant[] };

export const toStockedInhabitant = (dto: InhabitantDto): StockedInhabitant => ({
  id: Number(dto.id),
  species: dto.species ? { ...dto.species, id: Number(dto.species.id) } : null,
  count: dto.count === null ? null : Number(dto.count),
  name: dto.name,
  movedInOn: dto.movedInOn,
  removedOn: dto.removedOn,
  removalReason: dto.removalReason,
});

export const toStocking = (dto: InhabitantListDto): Stocking => ({
  current: dto.current.map(toStockedInhabitant),
  history: dto.history.map(toStockedInhabitant),
});

/** The values of an inhabitant form: a group or a named fish; count and days are the text typed. */
export type InhabitantDraft = {
  kind: "group" | "named";
  speciesId: number | null;
  count: string;
  name: string;
  movedInOn: string;
};

export type InhabitantProblem = "species" | "count" | "name" | "name_too_long" | "moved_in_invalid" | "moved_in_future";

/** The request body for an addition or a change. */
export type InhabitantInput = { speciesId?: number | null; count?: number | null; name?: string | null; movedInOn?: string | null };

const LOCAL_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Whether the text is a calendar date as YYYY-MM-DD. */
export const isCalendarDate = (text: string): boolean => {
  const match = LOCAL_DATE.exec(text);
  if (!match) return false;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
};

/**
 * The first problem of a form, or null. A group needs a species and a count from 1 to 10000; a named
 * fish needs a name of up to 80 characters and may have a species. The moved-in day is optional, a
 * calendar date and not after today.
 */
export function checkInhabitantDraft(draft: InhabitantDraft, today: string): InhabitantProblem | null {
  if (draft.kind === "named") {
    const name = draft.name.trim();
    if (name === "") return "name";
    if (name.length > INHABITANT_NAME_MAX_LENGTH) return "name_too_long";
  } else {
    if (draft.speciesId === null) return "species";
    const count = Number(draft.count.trim());
    if (draft.count.trim() === "" || !Number.isInteger(count) || count < 1 || count > INHABITANT_MAX_COUNT) return "count";
  }
  const day = draft.movedInOn.trim();
  if (day !== "") {
    if (!isCalendarDate(day)) return "moved_in_invalid";
    if (day > today) return "moved_in_future";
  }
  return null;
}

/** The request body of a form that passed checkInhabitantDraft; an empty moved-in day is left out. */
export function inhabitantInput(draft: InhabitantDraft): InhabitantInput {
  const movedInOn = draft.movedInOn.trim();
  const base: InhabitantInput = draft.kind === "named"
    ? { name: draft.name.trim(), speciesId: draft.speciesId }
    : { speciesId: draft.speciesId, count: Number(draft.count.trim()) };
  return movedInOn === "" ? base : { ...base, movedInOn };
}

/** The form for editing an inhabitant. */
export const draftOf = (inhabitant: StockedInhabitant): InhabitantDraft => ({
  kind: inhabitant.name === null ? "group" : "named",
  speciesId: inhabitant.species?.id ?? null,
  count: inhabitant.count === null ? "" : String(inhabitant.count),
  name: inhabitant.name ?? "",
  movedInOn: inhabitant.movedInOn ?? "",
});

/**
 * Checks the day an inhabitant left: ok, invalid (not a calendar date), future (after today) or
 * before_moved_in (earlier than the day it moved in). An empty text is ok and means today.
 */
export function checkRemovalDay(removedOn: string, movedInOn: string | null, today: string): "ok" | "invalid" | "future" | "before_moved_in" {
  const day = removedOn.trim();
  if (day === "") return "ok";
  if (!isCalendarDate(day)) return "invalid";
  if (day > today) return "future";
  return movedInOn !== null && day < movedInOn ? "before_moved_in" : "ok";
}

/** The name of a named fish, or the species of a group, for a list entry. */
export const inhabitantLabel = (inhabitant: StockedInhabitant): string | null =>
  inhabitant.name ?? inhabitant.species?.displayName ?? null;
