import { hasProvisionalContent, droppedVacations } from "@cichlids/client-core";
import type { ProvisionalContent, RegistrationOutcome, RegistrationPreview, RegistrationReason } from "@cichlids/client-core";
import { apiErrorKey } from "../lib/apiErrors";

/**
 * What the registration sheet shows. "reason": the action that needs an account and the way to
 * register. "working": the content of the device is handed to the account. "decision": the
 * account has a profile and the member chooses whether to take the content along. "done": the
 * result. "error": the handover failed.
 */
export type SheetState =
  | { step: "closed" }
  | { step: "reason"; reason: RegistrationReason }
  | { step: "working" }
  | { step: "decision"; preview: RegistrationPreview }
  | { step: "done"; result: "upgraded" | "moved" | "declined"; moved: ProvisionalContent; dropped: number }
  | { step: "error"; error: unknown };

export type SheetAction =
  | { type: "open"; reason: RegistrationReason }
  | { type: "close" }
  | { type: "working" }
  | { type: "decision"; preview: RegistrationPreview }
  | { type: "done"; result: "upgraded" | "moved" | "declined"; moved: ProvisionalContent; dropped: number }
  | { type: "fail"; error: unknown };

export const CLOSED: SheetState = { step: "closed" };

export function reduceSheet(state: SheetState, action: SheetAction): SheetState {
  switch (action.type) {
    case "open":
      return state.step === "closed" ? { step: "reason", reason: action.reason } : state;
    case "close":
      return state.step === "working" || state.step === "decision" ? state : CLOSED;
    case "working":
      return { step: "working" };
    case "decision":
      return { step: "decision", preview: action.preview };
    case "done":
      return { step: "done", result: action.result, moved: action.moved, dropped: action.dropped };
    case "fail":
      return { step: "error", error: action.error };
  }
}

/**
 * The sheet step for a finished registration. A registration that brought nothing over and
 * dropped nothing closes the sheet without a message; every other result is reported.
 */
export function outcomeStep(outcome: RegistrationOutcome): SheetAction {
  if (outcome.kind === "declined") return { type: "done", result: "declined", moved: outcome.preview.content, dropped: 0 };
  const moved = outcome.kind === "moved" ? outcome.moved : outcome.content;
  const dropped = outcome.kind === "moved" ? droppedVacations(outcome) : 0;
  if (!hasProvisionalContent(moved) && dropped === 0) return { type: "close" };
  return { type: "done", result: outcome.kind, moved, dropped };
}

/** The translation key for the failure of a registration. */
export function registrationErrorKey(error: unknown): string {
  return apiErrorKey(error) ?? "registration.error";
}
