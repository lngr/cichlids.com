import { createActor, waitFor } from "xstate";
import { expect, it, vi } from "vitest";
import { careMachine } from "./care.js";

const entry = { id: 7, tankId: 1, authorProfileId: 2, kind: "water_change" as const, occurredAt: "2026-10-15T12:00:00Z", recordedAt: "2026-10-15T12:00:00Z", amount: null };
it("logs once per pending tap and undo deletes the confirmed entry", async () => {
  const log = vi.fn(async () => entry), remove = vi.fn(async () => entry);
  const actor = createActor(careMachine, { input: { log, update: vi.fn(), remove } }).start();
  actor.send({ type: "LOG" }); actor.send({ type: "LOG" });
  await waitFor(actor, s => s.matches("confirmed"));
  expect(log).toHaveBeenCalledTimes(1);
  actor.send({ type: "UNDO" });
  await waitFor(actor, s => s.matches("idle"));
  expect(remove).toHaveBeenCalledWith(7);
  actor.stop();
});
it("expires undo after six seconds", async () => {
  vi.useFakeTimers();
  const actor = createActor(careMachine, { input: { log: async () => entry, update: vi.fn(), remove: vi.fn() } }).start();
  actor.send({ type: "LOG" });
  await vi.advanceTimersByTimeAsync(1);
  expect(actor.getSnapshot().matches("confirmed")).toBe(true);
  await vi.advanceTimersByTimeAsync(6000);
  expect(actor.getSnapshot().matches("idle")).toBe(true);
  actor.send({ type: "UNDO" });
  expect(actor.getSnapshot().matches("idle")).toBe(true);
  actor.stop(); vi.useRealTimers();
});
it("keeps the editor after a failed update and allows retry", async () => {
  const update = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue(entry);
  const actor = createActor(careMachine, { input: { log: vi.fn(), update, remove: vi.fn() } }).start();
  actor.send({ type: "EDIT", entry });
  actor.send({ type: "SAVE", amount: { value: 25, unit: "percent" } });
  await waitFor(actor, s => s.matches("editing") && s.context.error !== null);
  actor.send({ type: "SAVE", amount: { value: 30, unit: "percent" } });
  await waitFor(actor, s => s.matches("idle"));
  expect(update).toHaveBeenLastCalledWith(7, { value: 30, unit: "percent" });
  actor.stop();
});
