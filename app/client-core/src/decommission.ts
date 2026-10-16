import type { DecommissionScope, DecommissioningDto, ImageUrlsDto, TankOperationDto } from "./generated/types.gen.js";

export type { DecommissionScope };

/** The photo URLs of one image in the sizes the API offers; a size can be missing. */
export type DecommissionImage = ImageUrlsDto;

/**
 * One decommissioning of a tank in the local dates of its owner. The start image shows the empty tank and the
 * end image the filled tank; the end date and the end image are null while the decommissioning is active.
 */
export type Decommissioning = {
  id: number;
  scope: DecommissionScope;
  startDate: string;
  endDate: string | null;
  active: boolean;
  startImage: DecommissionImage | null;
  endImage: DecommissionImage | null;
};

/**
 * The operating time of a tank without its decommissioned days, the whole months they span and the
 * decommissionings, the newest first.
 */
export type TankOperation = {
  tankId: number;
  operatingDays: number;
  operatingMonths: number;
  decommissionedDays: number;
  decommissioned: boolean;
  items: Decommissioning[];
};

const toDecommissioning = (dto: DecommissioningDto): Decommissioning => ({
  id: Number(dto.id), scope: dto.scope, startDate: dto.startDate, endDate: dto.endDate, active: dto.active,
  startImage: dto.startImage, endImage: dto.endImage,
});

export const toTankOperation = (dto: TankOperationDto): TankOperation => ({
  tankId: Number(dto.tankId), operatingDays: Number(dto.operatingDays), operatingMonths: Number(dto.operatingMonths),
  decommissionedDays: Number(dto.decommissionedDays), decommissioned: dto.decommissioned, items: dto.items.map(toDecommissioning),
});

/** The decommissioning that is active, or null when the tank is in operation. */
export const activeDecommissioning = (operation: TankOperation): Decommissioning | null =>
  operation.items.find(item => item.active) ?? null;

/** The months from which the operating time is shown in years. */
const YEARS_FROM_MONTHS = 24;

/** The unit and the count the operating time is shown in: days below one month, months below two years, years from there. */
export function operatingTime(operation: TankOperation): { unit: "days" | "months" | "years"; count: number } {
  if (operation.operatingMonths >= YEARS_FROM_MONTHS) return { unit: "years", count: Math.floor(operation.operatingMonths / 12) };
  if (operation.operatingMonths >= 1) return { unit: "months", count: operation.operatingMonths };
  return { unit: "days", count: operation.operatingDays };
}
