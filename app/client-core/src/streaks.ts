import type { TankStreaksDto, MeStreaksDto, VacationDto, VacationShareDto, WaterChangeStreakDto, TimeLapseStreakDto } from "./generated/types.gen.js";

export type WaterChangeStreakState = "no_history" | "running" | "paused" | "broken";

/** The water change streak of a tank. Deadline, freezesLeft and freezesPerYear are set for the owner only. */
export type WaterChangeStreak = {
  state: WaterChangeStreakState;
  current: number;
  currentVerified: number;
  longest: number;
  lastWaterChange: string | null;
  deadline: string | null;
  freezesLeft: number | null;
  freezesPerYear: number | null;
};

/** The time-lapse streak of a tank over ISO weeks. Paused and keepingPhotoIds are set for the owner only. */
export type TimeLapseStreak = {
  current: number;
  longest: number;
  thisWeekHasPhoto: boolean;
  thisWeekOpen: boolean;
  weekStart: string;
  weekEnd: string;
  paused: boolean | null;
  keepingPhotoIds: number[] | null;
};

/** A vacation in the local dates of the member; endDate is null while it is active. */
export type Vacation = { id: number; startDate: string; endDate: string | null; active: boolean };

/** The streaks of one tank a viewer may see; the water change streak is null without access to the care stream. */
export type TankStreaks = { tankId: number; waterChange: WaterChangeStreak | null; timeLapse: TimeLapseStreak | null; vacations: Vacation[] };

export type OwnTankStreaks = { tankId: number; title: string; waterChange: WaterChangeStreak; timeLapse: TimeLapseStreak };

/** The streaks of every tank of the member with the vacations of the member. */
export type MeStreaks = { freezesPerYear: number; vacations: Vacation[]; tanks: OwnTankStreaks[] };

/** The days a member spent on ended vacations within a window of days. */
export type VacationShare = { vacationDays: number; windowDays: number; fraction: number; pastVacationCount: number };

const optionalNumber = (value: number | string | null): number | null => (value === null ? null : Number(value));

const toWaterChange = (dto: WaterChangeStreakDto): WaterChangeStreak => ({
  state: dto.state, current: Number(dto.current), currentVerified: Number(dto.currentVerified), longest: Number(dto.longest),
  lastWaterChange: dto.lastWaterChange, deadline: dto.deadline, freezesLeft: optionalNumber(dto.freezesLeft), freezesPerYear: optionalNumber(dto.freezesPerYear),
});

const toTimeLapse = (dto: TimeLapseStreakDto): TimeLapseStreak => ({
  current: Number(dto.current), longest: Number(dto.longest), thisWeekHasPhoto: dto.thisWeekHasPhoto, thisWeekOpen: dto.thisWeekOpen,
  weekStart: dto.weekStart, weekEnd: dto.weekEnd, paused: dto.paused, keepingPhotoIds: dto.keepingPhotoIds?.map(Number) ?? null,
});

export const toVacation = (dto: VacationDto): Vacation => ({ id: Number(dto.id), startDate: dto.startDate, endDate: dto.endDate, active: dto.active });

export const toTankStreaks = (dto: TankStreaksDto): TankStreaks => ({
  tankId: Number(dto.tankId),
  waterChange: dto.waterChange ? toWaterChange(dto.waterChange) : null,
  timeLapse: dto.timeLapse ? toTimeLapse(dto.timeLapse) : null,
  vacations: dto.vacations.map(toVacation),
});

export const toMeStreaks = (dto: MeStreaksDto): MeStreaks => ({
  freezesPerYear: Number(dto.freezesPerYear),
  vacations: dto.vacations.map(toVacation),
  tanks: dto.tanks.map(tank => ({
    tankId: Number(tank.tankId), title: tank.title, waterChange: toWaterChange(tank.waterChange), timeLapse: toTimeLapse(tank.timeLapse),
  })),
});

export const toVacationShare = (dto: VacationShareDto): VacationShare => ({
  vacationDays: Number(dto.vacationDays), windowDays: Number(dto.windowDays), fraction: Number(dto.fraction), pastVacationCount: Number(dto.pastVacationCount),
});

/** The active vacation, or null when none is running. */
export const activeVacation = (vacations: readonly Vacation[]): Vacation | null => vacations.find(vacation => vacation.active) ?? null;

const dayNumber = (date: string): number => {
  const [year, month, day] = date.split("-").map(Number);
  return Math.round(Date.UTC(year!, month! - 1, day!) / 86_400_000);
};

/** The days from a local date to a deadline: 0 on the deadline day, negative after it. */
export const daysUntilDeadline = (deadline: string, today: string): number => dayNumber(deadline) - dayNumber(today);

/**
 * Whether deleting the series photo breaks the time-lapse streak, because it is the only photo of a
 * week of the current run. Only the owner receives the list this reads.
 */
export const seriesPhotoKeepsStreak = (streak: TimeLapseStreak | null, mediaItemId: number): boolean =>
  streak?.keepingPhotoIds?.includes(mediaItemId) ?? false;

/** Whether a tank has a water change or a counted week, so its streaks are worth showing. */
export const hasStreakHistory = (tank: { waterChange: WaterChangeStreak | null; timeLapse: TimeLapseStreak | null }): boolean =>
  (tank.waterChange !== null && tank.waterChange.state !== "no_history") || (tank.timeLapse?.longest ?? 0) > 0;
