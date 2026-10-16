import { createActor, waitFor } from "xstate";
import { expect, it, vi } from "vitest";
import { ApiError } from "./client.js";
import type { TankTimeLapse, TankTimeLapsePhoto } from "./client.js";
import {
  DEFAULT_OVERLAY_OPACITY, latestSeriesPhotoUrl, overlayOpacity, timeLapseCameraMachine,
} from "./timeLapseCamera.js";

const added: TankTimeLapsePhoto = {
  mediaItemId: 9, image: { thumb: "t", small: "s", medium: "m", large: "l" }, takenAt: null, publishedAt: "2026-10-16T08:00:00Z",
};
const start = (previous: string | null, upload = vi.fn(async () => added)) =>
  ({ upload, actor: createActor(timeLapseCameraMachine, { input: { previous, upload } }).start() });
const shot = new Blob(["x"]);

it("shows the previous photo at about 40 percent and lets the member change and hide it", () => {
  const { actor } = start("https://img/last.jpg");
  expect(DEFAULT_OVERLAY_OPACITY).toBe(0.4);
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0.4);
  actor.send({ type: "SET_OPACITY", value: 0.75 });
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0.75);
  actor.send({ type: "SET_OPACITY", value: 5 });
  expect(actor.getSnapshot().context.opacity).toBe(1);
  actor.send({ type: "SET_OPACITY", value: -1 });
  expect(actor.getSnapshot().context.opacity).toBe(0);
  actor.send({ type: "SET_OPACITY", value: Number.NaN });
  expect(actor.getSnapshot().context.opacity).toBe(0);
  actor.send({ type: "SET_OPACITY", value: 0.6 });
  actor.send({ type: "TOGGLE_OVERLAY" });
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0);
  actor.send({ type: "TOGGLE_OVERLAY" });
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0.6);
});

it("has no overlay without a previous photo", () => {
  const { actor } = start(null);
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0);
  actor.send({ type: "TOGGLE_OVERLAY" });
  expect(actor.getSnapshot().context.overlayVisible).toBe(false);
  actor.send({ type: "SET_OPACITY", value: 0.9 });
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0);
});

it("reviews a shot, lets the member retake it and keeps the overlay settings", () => {
  const { actor, upload } = start("https://img/last.jpg");
  actor.send({ type: "SET_OPACITY", value: 0.7 });
  actor.send({ type: "USE" });
  expect(actor.getSnapshot().matches("framing")).toBe(true);
  actor.send({ type: "SHOT", file: shot });
  expect(actor.getSnapshot().matches("reviewing")).toBe(true);
  expect(actor.getSnapshot().context.shot).toBe(shot);
  expect(overlayOpacity(actor.getSnapshot().context)).toBe(0.7);
  actor.send({ type: "RETAKE" });
  expect(actor.getSnapshot().matches("framing")).toBe(true);
  expect(actor.getSnapshot().context.shot).toBeNull();
  expect(actor.getSnapshot().context.opacity).toBe(0.7);
  expect(upload).not.toHaveBeenCalled();
});

it("uploads the used shot once and finishes with the added photo", async () => {
  const { actor, upload } = start("https://img/last.jpg");
  actor.send({ type: "SHOT", file: shot });
  actor.send({ type: "USE" });
  actor.send({ type: "USE" });
  await waitFor(actor, s => s.status === "done");
  expect(upload).toHaveBeenCalledTimes(1);
  expect(upload).toHaveBeenCalledWith(shot, false);
  expect(actor.getSnapshot().output).toBe(added);
});

it("passes on whether the camera of the app took the shot and forgets it on a retake", async () => {
  const { actor, upload } = start(null);
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  expect(actor.getSnapshot().context.shotInApp).toBe(true);
  actor.send({ type: "SHOT", file: shot });
  expect(actor.getSnapshot().context.shotInApp).toBe(false);
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  actor.send({ type: "RETAKE" });
  expect(actor.getSnapshot().context.shotInApp).toBe(false);
  actor.send({ type: "SHOT", file: shot, capturedInApp: true });
  actor.send({ type: "USE" });
  await waitFor(actor, s => s.status === "done");
  expect(upload).toHaveBeenCalledWith(shot, true);
});

it("returns to the review with the error when the upload fails and allows another try", async () => {
  const failure = new ApiError("Too many", 429, "upload_rate_limited");
  const upload = vi.fn().mockRejectedValueOnce(failure).mockResolvedValueOnce(added);
  const { actor } = start(null, upload);
  actor.send({ type: "SHOT", file: shot });
  actor.send({ type: "USE" });
  await waitFor(actor, s => s.matches("reviewing"));
  expect(actor.getSnapshot().context.error).toBe(failure);
  actor.send({ type: "USE" });
  expect(actor.getSnapshot().context.error).toBeNull();
  await waitFor(actor, s => s.status === "done");
  expect(upload).toHaveBeenCalledTimes(2);
});

it("takes the newest photo of the series as the overlay", () => {
  const photo = (id: number, image: TankTimeLapsePhoto["image"]): TankTimeLapsePhoto => ({ ...added, mediaItemId: id, image });
  const series = (photos: TankTimeLapsePhoto[]): TankTimeLapse => ({ isShowcase: false, photos, posts: [] });
  expect(latestSeriesPhotoUrl(series([]))).toBeNull();
  expect(latestSeriesPhotoUrl(series([photo(1, { large: "old" }), photo(2, { medium: "m2", small: "s2" })]))).toBe("m2");
  expect(latestSeriesPhotoUrl(series([photo(1, { large: "l1" }), photo(2, {})]))).toBe("l1");
});
