import { ApiError } from "./client.js";
import type { CichlidsClient, LocalTime } from "./client.js";
import { REGISTRATION_REQUIRED_CODE } from "./registration.js";
import type { ProvisionalContent, RegistrationMove, RegistrationPreview } from "./registration.js";

/** What a member of a device account tried when the registration was asked for; "generic" covers answers without a known cause. */
export const REGISTRATION_REASONS = [
  "follow",
  "comment",
  "rate",
  "story",
  "publish",
  "visibility",
  "profileImages",
  "audience",
  "register",
  "generic",
] as const;

export type RegistrationReason = (typeof REGISTRATION_REASONS)[number];

/** Whether the error is the answer of the API for an action that needs a registered account. */
export function isRegistrationRequired(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403 && error.code === REGISTRATION_REQUIRED_CODE;
}

/** Whether a registration brings anything over; an installation without tanks, entries, media, to-dos and vacations has no content. */
export function hasProvisionalContent(content: ProvisionalContent): boolean {
  return content.tanks > 0 || content.careEntries > 0 || content.mediaItems > 0 || content.todos > 0 || content.vacations > 0;
}

/** The vacations a move leaves behind, both for overlapping and for repairing a period. */
export function droppedVacations(result: { vacationsDropped: number; vacationsDroppedRepair: number }): number {
  return result.vacationsDropped + result.vacationsDroppedRepair;
}

/**
 * How a registration ended. "upgraded": the profile of the installation became the profile of the
 * account. "moved": the content went into the existing profile of the account. "declined": the
 * member kept the content on the device; the preview names what stays behind.
 */
export type RegistrationOutcome =
  | { kind: "upgraded"; me: Awaited<ReturnType<CichlidsClient["registration"]["upgrade"]>> }
  | ({ kind: "moved" } & RegistrationMove)
  | { kind: "declined"; preview: RegistrationPreview };

/**
 * Hands the content of the installation to the signed-in account. An account without a profile
 * is upgraded. An account with a profile receives the content after decide answers true for the
 * preview; an installation without content is moved without asking, which retires its key.
 */
export async function completeRegistration(
  registration: CichlidsClient["registration"],
  local: LocalTime,
  decide: (preview: RegistrationPreview) => Promise<boolean>,
): Promise<RegistrationOutcome> {
  const preview = await registration.preview(local);
  if (preview.mode === "upgrade") return { kind: "upgraded", me: await registration.upgrade() };
  if (hasProvisionalContent(preview.content) && !(await decide(preview))) return { kind: "declined", preview };
  return { kind: "moved", ...(await registration.move(local)) };
}
