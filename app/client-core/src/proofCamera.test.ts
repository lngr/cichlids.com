import { createActor, waitFor } from "xstate";
import { expect, it, vi } from "vitest";
import type { TankOperation } from "./decommission.js";
import { proofCameraMachine } from "./proofCamera.js";

const done: TankOperation = { tankId: 7, operatingDays: 1, operatingMonths: 0, decommissionedDays: 0, decommissioned: true, items: [] };
const start = (upload = vi.fn(async () => done)) => ({ upload, actor: createActor(proofCameraMachine, { input: { upload } }).start() });
const shot = new Blob(["x"]);

it("frames, reviews a shot, lets the member retake it and sends the used shot with its origin", async () => {
  const { actor, upload } = start();
  expect(actor.getSnapshot().matches("framing")).toBe(true);
  actor.send({ type: "USE" });
  expect(actor.getSnapshot().matches("framing")).toBe(true);
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  expect(actor.getSnapshot().matches("reviewing")).toBe(true);
  actor.send({ type: "RETAKE" });
  expect(actor.getSnapshot().matches("framing")).toBe(true);
  expect(actor.getSnapshot().context.shot).toBeNull();
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  actor.send({ type: "USE" });
  const finished = await waitFor(actor, snapshot => snapshot.status === "done");
  expect(upload).toHaveBeenCalledWith(shot, true);
  expect(finished.output).toBe(done);
});

it("treats a picked shot as not taken with the camera of the app", async () => {
  const { actor, upload } = start();
  actor.send({ type: "SHOT", file: shot });
  actor.send({ type: "USE" });
  await waitFor(actor, snapshot => snapshot.status === "done");
  expect(upload).toHaveBeenCalledWith(shot, false);
});

it("returns to the review with the error when the upload fails and sends again on a second use", async () => {
  const upload = vi.fn().mockRejectedValueOnce(new Error("refused")).mockResolvedValue(done);
  const { actor } = start(upload);
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  actor.send({ type: "USE" });
  await waitFor(actor, snapshot => snapshot.matches("reviewing") && snapshot.context.error !== null);
  expect(actor.getSnapshot().context.error).toBeInstanceOf(Error);
  actor.send({ type: "USE" });
  expect(actor.getSnapshot().context.error).toBeNull();
  await waitFor(actor, snapshot => snapshot.status === "done");
  expect(upload).toHaveBeenCalledTimes(2);
});
