import type { MeDto, ProvisionalContentCounts, ProvisionalMoveDto, ProvisionalPreviewDto } from "./generated/types.gen.js";

/** The error code of an action that only a registered account may take. */
export const REGISTRATION_REQUIRED_CODE = "registration_required";

/**
 * How the content of the installation reaches the signed-in account: upgrade when the account has no
 * profile yet and the profile of the installation becomes it, move when the account has a profile
 * that receives the content.
 */
export type RegistrationMode = "upgrade" | "move";

/** The amounts of content of a provisional profile. */
export type ProvisionalContent = {
  tanks: number;
  careEntries: number;
  mediaItems: number;
  todos: number;
  vacations: number;
};

/**
 * What handing the content to the signed-in account does: the mode, the amounts that would arrive,
 * the vacations that overlap vacations of the account and are dropped, the vacations dropped because
 * they would repair a period of a tank of the account, and the display name of the profile that
 * receives the content (null for an upgrade).
 */
export type RegistrationPreview = {
  mode: RegistrationMode;
  content: ProvisionalContent;
  vacationsDropped: number;
  vacationsDroppedRepair: number;
  targetDisplayName: string | null;
};

/** The result of a move: the amounts that arrived, the vacations dropped for overlap and for repairing, and the profile of the account. */
export type RegistrationMove = {
  moved: ProvisionalContent;
  vacationsDropped: number;
  vacationsDroppedRepair: number;
  me: MeDto;
};

const toContent = (counts: ProvisionalContentCounts): ProvisionalContent => ({
  tanks: Number(counts.tanks),
  careEntries: Number(counts.careEntries),
  mediaItems: Number(counts.mediaItems),
  todos: Number(counts.todos),
  vacations: Number(counts.vacations),
});

export const toRegistrationPreview = (dto: ProvisionalPreviewDto): RegistrationPreview => ({
  mode: dto.mode,
  content: toContent(dto.content),
  vacationsDropped: Number(dto.vacationsDropped),
  vacationsDroppedRepair: Number(dto.vacationsDroppedRepair),
  targetDisplayName: dto.targetDisplayName,
});

export const toRegistrationMove = (dto: ProvisionalMoveDto): RegistrationMove => ({
  moved: toContent(dto.moved),
  vacationsDropped: Number(dto.vacationsDropped),
  vacationsDroppedRepair: Number(dto.vacationsDroppedRepair),
  me: dto.me,
});
