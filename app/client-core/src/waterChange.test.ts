import { createActor, waitFor } from "xstate";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { WaterChangeFinished, WaterChangeSession } from "./client.js";
import {
  drainingSeconds, formatElapsed, nextWaterChangeStage, totalSeconds, waterChangeMachine, type StoredWaterChange, type WaterChangeStage,
} from "./waterChange.js";

type Step = WaterChangeSession["steps"][number];
const T0 = Date.parse("2026-10-16T09:00:00Z");
const iso = (offsetMs: number) => new Date(T0 + offsetMs).toISOString();
const step = (name: Step["step"], state: Step["state"] = "done", offsetMs = 0): Step => ({ step: name, state, at: iso(offsetMs) });
const session = (steps: Step[] = [], extra: Partial<WaterChangeSession> = {}): WaterChangeSession => ({
  id: 5, tankId: 42, startedAt: iso(0), expiresAt: iso(86400000), finishedAt: null, durationSeconds: null, steps,
  drainedPhoto: null, refilledPhoto: null, previousDrainedPhoto: null, minDurationSeconds: 600, ...extra,
});
const photo = { urls: { thumb: "/t", small: null, medium: null, large: null }, width: 1, height: 1, capturedInApp: true, receivedAt: iso(0), takenAt: null };
const finished = (): WaterChangeFinished => ({ session: session([], { finishedAt: iso(1000), durationSeconds: 1 }), entry: { id: 9 } as never });

function setup(start: WaterChangeSession = session(), stored: StoredWaterChange | null = null) {
  let nowMs = T0;
  const saved: Array<StoredWaterChange | null> = [];
  const effects = {
    start: vi.fn(async () => start),
    markStep: vi.fn(async (_id: number, name: Step["step"], state: Step["state"]) => session([step(name, state, nowMs - T0)])),
    setPhoto: vi.fn(async (_id: number, slot: "drained" | "refilled") => session([], slot === "drained" ? { drainedPhoto: photo } : { refilledPhoto: photo })),
    finish: vi.fn(async () => finished()),
    cancel: vi.fn(async () => undefined),
    storage: { load: vi.fn(async () => stored), save: vi.fn(async (value: StoredWaterChange | null) => { saved.push(value); }) },
    now: () => nowMs,
  };
  const actor = createActor(waterChangeMachine, { input: effects }).start();
  return { actor, effects, saved, advance: (ms: number) => { nowMs += ms; } };
}
const stageIn = (value: unknown): string | undefined => {
  const running = (value as { running?: string | Record<string, string> }).running;
  return typeof running === "object" ? Object.keys(running)[0] : running;
};
const stageOf = (actor: ReturnType<typeof setup>["actor"]) => stageIn(actor.getSnapshot().value);
const inStage = (stage: WaterChangeStage) => (snapshot: { value: unknown }) => stageIn(snapshot.value) === stage;

beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); });

it("derives the next stage from the steps and photos of the session", () => {
  const cases: Array<[WaterChangeSession, WaterChangeStage]> = [
    [session(), "heaterFilterOff"],
    [session([step("heater_filter_off")]), "draining"],
    [session([step("heater_filter_off", "skipped")]), "draining"],
    [session([step("heater_filter_off"), step("draining_started")]), "drainedPhoto"],
    [session([step("heater_filter_off"), step("draining_started")], { drainedPhoto: photo }), "tips"],
    [session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped")]), "tips"],
    [session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped"), step("conditioner", "skipped")]), "refill"],
    [session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped"), step("temperature_matched"), step("refill_started")]), "heaterFilterOn"],
    [session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped"), step("conditioner"), step("refill_started"), step("heater_filter_on")]), "refilledPhoto"],
    [session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped"), step("conditioner"), step("refill_started"), step("heater_filter_on"), step("refilled_photo", "skipped")]), "amount"],
  ];
  for (const [value, stage] of cases) expect(nextWaterChangeStage(value)).toBe(stage);
});

it("starts the session on the server and shows the first stage", async () => {
  const { actor, effects } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  expect(effects.start).toHaveBeenCalledTimes(1);
  expect(actor.getSnapshot().context.session?.id).toBe(5);
});

it("marks a step as done or skipped in order and moves on at once", async () => {
  const { actor, effects } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  actor.send({ type: "DONE" });
  expect(stageOf(actor)).toBe("draining");
  actor.send({ type: "SKIP" });
  expect(stageOf(actor)).toBe("drainedPhoto");
  await vi.advanceTimersByTimeAsync(0);
  expect(effects.markStep.mock.calls.map(c => c.slice(1))).toEqual([["heater_filter_off", "done"], ["draining_started", "skipped"]]);
});

it("never blocks on a failing step mark and counts the failure", async () => {
  const { actor, effects } = setup();
  effects.markStep.mockRejectedValue(new Error("offline"));
  await waitFor(actor, inStage("heaterFilterOff"));
  actor.send({ type: "DONE" });
  actor.send({ type: "DONE" });
  expect(stageOf(actor)).toBe("drainedPhoto");
  await vi.advanceTimersByTimeAsync(0);
  expect(actor.getSnapshot().context.syncFailures).toBe(2);
  expect(actor.getSnapshot().context.drainStartedAtMs).not.toBeNull();
});

it("lets every step be skipped and still finish", async () => {
  const { actor, effects } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  for (const stage of ["heaterFilterOff", "draining", "drainedPhoto", "tips", "refill", "heaterFilterOn", "refilledPhoto"]) {
    expect(stageOf(actor)).toBe(stage);
    actor.send({ type: "SKIP" });
  }
  expect(stageOf(actor)).toBe("amount");
  actor.send({ type: "FINISH" });
  const done = await waitFor(actor, s => s.status === "done");
  expect(effects.finish).toHaveBeenCalledWith(5, null);
  expect(done.output).toEqual({ finished: finished() });
});

it("records the chosen tips and skips the unmarked ones when moving on", async () => {
  const { actor, effects } = setup(session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped")]));
  await waitFor(actor, inStage("tips"));
  actor.send({ type: "TIP", step: "conditioner", state: "done" });
  actor.send({ type: "DONE" });
  expect(stageOf(actor)).toBe("refill");
  await vi.advanceTimersByTimeAsync(0);
  expect(effects.markStep.mock.calls.map(c => c.slice(1))).toEqual([["conditioner", "done"], ["temperature_matched", "skipped"]]);
});

it("keeps a running timer from the start of draining", async () => {
  const { actor, advance } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  actor.send({ type: "DONE" });
  advance(1000);
  actor.send({ type: "DONE" });
  await vi.advanceTimersByTimeAsync(0);
  advance(125000);
  await vi.advanceTimersByTimeAsync(1000);
  const { context } = actor.getSnapshot();
  expect(drainingSeconds(context)).toBe(125);
  expect(totalSeconds(context)).toBe(126);
  expect(formatElapsed(125)).toBe("2:05");
  expect(formatElapsed(3725)).toBe("1:02:05");
  expect(formatElapsed(-4)).toBe("0:00");
});

it("takes the draining start of a resumed session from the server step", async () => {
  const resumed = session([step("heater_filter_off"), step("draining_started", "done", 60000)]);
  const { actor, advance } = setup(resumed);
  advance(5 * 60000);
  await waitFor(actor, inStage("drainedPhoto"));
  await vi.advanceTimersByTimeAsync(1000);
  expect(drainingSeconds(actor.getSnapshot().context)).toBe(5 * 60 - 60);
});

it("resumes at the stage the member reached when the app was closed", async () => {
  const hint: StoredWaterChange = { sessionId: 5, stage: "amount" };
  const { actor } = setup(session([step("heater_filter_off")]), hint);
  await waitFor(actor, inStage("amount"));
  const other = setup(session([step("heater_filter_off")]), { sessionId: 99, stage: "amount" });
  await waitFor(other.actor, inStage("draining"));
});

it("remembers the stage and forgets it when the water change ends", async () => {
  const { actor, saved } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  actor.send({ type: "DONE" });
  await vi.advanceTimersByTimeAsync(0);
  expect(saved.at(-1)).toEqual({ sessionId: 5, stage: "draining" });
  for (const _ of ["draining", "drainedPhoto", "tips", "refill", "heaterFilterOn", "refilledPhoto"]) actor.send({ type: "SKIP" });
  actor.send({ type: "FINISH" });
  await waitFor(actor, s => s.status === "done");
  expect(saved.at(-1)).toBeNull();
});

it("uploads a photo as app evidence and continues, or continues without it", async () => {
  const { actor, effects } = setup(session([step("heater_filter_off"), step("draining_started")]));
  await waitFor(actor, inStage("drainedPhoto"));
  const file = new Blob(["x"]);
  actor.send({ type: "PHOTO", file });
  await waitFor(actor, inStage("tips"));
  expect(effects.setPhoto).toHaveBeenCalledWith(5, "drained", file, true);
  expect(actor.getSnapshot().context.session?.drainedPhoto).not.toBeNull();
});

it("stays on the photo stage after a failed upload and lets the member skip it", async () => {
  const { actor, effects } = setup(session([step("heater_filter_off"), step("draining_started")]));
  effects.setPhoto.mockRejectedValueOnce(new Error("offline"));
  await waitFor(actor, inStage("drainedPhoto"));
  actor.send({ type: "PHOTO", file: new Blob(["x"]) });
  await waitFor(actor, s => (s.value as { running?: Record<string, string> }).running?.drainedPhoto === "ready" && s.context.error !== null);
  actor.send({ type: "SKIP" });
  expect(stageOf(actor)).toBe("tips");
  expect(actor.getSnapshot().context.error).toBeNull();
});

it("reports a failed finish and keeps the amount stage", async () => {
  const { actor, effects } = setup(session([step("heater_filter_off"), step("draining_started"), step("drained_photo", "skipped"), step("conditioner", "skipped"),
    step("refill_started"), step("heater_filter_on"), step("refilled_photo", "skipped")]));
  effects.finish.mockRejectedValueOnce(new Error("offline"));
  await waitFor(actor, inStage("amount"));
  actor.send({ type: "SET_AMOUNT", amount: { value: 30, unit: "percent" } });
  actor.send({ type: "FINISH" });
  await waitFor(actor, s => inStage("amount")(s) && s.context.error !== null);
  actor.send({ type: "FINISH" });
  await waitFor(actor, s => s.status === "done");
  expect(effects.finish).toHaveBeenLastCalledWith(5, { value: 30, unit: "percent" });
});

it("cancels the session and forgets the stage", async () => {
  const { actor, effects, saved } = setup();
  await waitFor(actor, inStage("heaterFilterOff"));
  actor.send({ type: "CANCEL" });
  const done = await waitFor(actor, s => s.status === "done");
  expect(effects.cancel).toHaveBeenCalledWith(5);
  expect(done.output).toEqual({ finished: null });
  expect(saved.at(-1)).toBeNull();
});

it("offers a retry when the session cannot be started", async () => {
  const { actor, effects } = setup();
  effects.start.mockRejectedValueOnce(new Error("offline"));
  const next = createActor(waterChangeMachine, { input: effects }).start();
  await waitFor(next, s => s.matches("failed"));
  next.send({ type: "RETRY" });
  await waitFor(next, inStage("heaterFilterOff"));
  actor.stop();
});
