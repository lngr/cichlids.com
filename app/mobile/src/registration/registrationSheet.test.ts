import { describe, expect, it } from "vitest";
import { ApiError, type RegistrationOutcome, type RegistrationPreview } from "@cichlids/client-core";
import { CLOSED, outcomeStep, reduceSheet, registrationErrorKey, type SheetState } from "./registrationSheet";

const content = { tanks: 2, careEntries: 14, mediaItems: 3, todos: 0, vacations: 0 };
const none = { tanks: 0, careEntries: 0, mediaItems: 0, todos: 0, vacations: 0 };
const preview: RegistrationPreview = { mode: "move", content, vacationsDropped: 1, vacationsDroppedRepair: 0, targetDisplayName: "Tanja" };
const me = { profile: { id: 1 }, roles: [], provisional: false } as never;

describe("reduceSheet", () => {
  it("opens with the reason", () => {
    expect(reduceSheet(CLOSED, { type: "open", reason: "follow" })).toEqual({ step: "reason", reason: "follow" });
  });

  it("keeps the open sheet when another reason arrives", () => {
    const open: SheetState = { step: "reason", reason: "follow" };
    expect(reduceSheet(open, { type: "open", reason: "comment" })).toBe(open);
    const working: SheetState = { step: "working" };
    expect(reduceSheet(working, { type: "open", reason: "comment" })).toBe(working);
  });

  it("closes from the reason, the result and the error", () => {
    expect(reduceSheet({ step: "reason", reason: "follow" }, { type: "close" })).toEqual(CLOSED);
    expect(reduceSheet({ step: "done", result: "declined", moved: none, dropped: 0 }, { type: "close" })).toEqual(CLOSED);
    expect(reduceSheet({ step: "error", error: new Error("x") }, { type: "close" })).toEqual(CLOSED);
  });

  it("does not close while the content is handed over or the member decides", () => {
    expect(reduceSheet({ step: "working" }, { type: "close" })).toEqual({ step: "working" });
    const deciding: SheetState = { step: "decision", preview };
    expect(reduceSheet(deciding, { type: "close" })).toBe(deciding);
  });

  it("goes through working, decision and result", () => {
    let state = reduceSheet({ step: "reason", reason: "follow" }, { type: "working" });
    expect(state).toEqual({ step: "working" });
    state = reduceSheet(state, { type: "decision", preview });
    expect(state).toEqual({ step: "decision", preview });
    state = reduceSheet(state, { type: "working" });
    expect(state.step).toBe("working");
    state = reduceSheet(state, { type: "done", result: "moved", moved: content, dropped: 1 });
    expect(state).toEqual({ step: "done", result: "moved", moved: content, dropped: 1 });
  });

  it("opens on a working step although the sheet was closed, since a handover can start without it", () => {
    expect(reduceSheet(CLOSED, { type: "working" })).toEqual({ step: "working" });
  });

  it("shows a failure and lets the member try again", () => {
    const failed = reduceSheet({ step: "working" }, { type: "fail", error: new ApiError("x", 409, "account_has_profile") });
    expect(failed.step).toBe("error");
    expect(reduceSheet(failed, { type: "working" })).toEqual({ step: "working" });
  });
});

describe("outcomeStep", () => {
  it("closes the sheet silently for an upgrade or move of an installation without content", () => {
    const upgraded: RegistrationOutcome = { kind: "upgraded", me, content: none };
    expect(outcomeStep(upgraded)).toEqual({ type: "close" });
    const moved: RegistrationOutcome = { kind: "moved", moved: none, vacationsDropped: 0, vacationsDroppedRepair: 0, me };
    expect(outcomeStep(moved)).toEqual({ type: "close" });
  });

  it("reports an upgrade with the content that stays in the account", () => {
    expect(outcomeStep({ kind: "upgraded", me, content })).toEqual({ type: "done", result: "upgraded", moved: content, dropped: 0 });
  });

  it("reports a move with the amounts and the vacations that were dropped", () => {
    const moved: RegistrationOutcome = { kind: "moved", moved: content, vacationsDropped: 1, vacationsDroppedRepair: 2, me };
    expect(outcomeStep(moved)).toEqual({ type: "done", result: "moved", moved: content, dropped: 3 });
  });

  it("reports vacations that were dropped even when nothing else moved", () => {
    const moved: RegistrationOutcome = { kind: "moved", moved: none, vacationsDropped: 1, vacationsDroppedRepair: 0, me };
    expect(outcomeStep(moved)).toMatchObject({ type: "done", dropped: 1 });
  });

  it("reports that the content stays on the device after a no", () => {
    const declined: RegistrationOutcome = { kind: "declined", preview };
    expect(outcomeStep(declined)).toEqual({ type: "done", result: "declined", moved: content, dropped: 0 });
  });
});

describe("registrationErrorKey", () => {
  it("names the account that already has a profile and the suspension", () => {
    expect(registrationErrorKey(new ApiError("x", 409, "account_has_profile"))).toBe("errors.account_has_profile");
    expect(registrationErrorKey(new ApiError("x", 403, "account_suspended"))).toBe("errors.account_suspended");
  });

  it("names a refused device proof", () => {
    expect(registrationErrorKey(new ApiError("x", 403, "device_auth_refused"))).toBe("errors.device_auth_refused");
  });

  it("falls back to the connection and the general message", () => {
    expect(registrationErrorKey(new ApiError("x", 0))).toBe("errors.connection");
    expect(registrationErrorKey(new Error("x"))).toBe("registration.error");
  });
});
