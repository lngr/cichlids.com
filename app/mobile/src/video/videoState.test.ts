import { describe, expect, it } from "vitest";
import type { Video } from "@cichlids/client-core";
import { describeUpload, formatDuration, hasProcessing, uploadPercent, videoStateOf } from "./videoState";

const video = (state: Video["state"]): Video => ({ id: 1, state, durationSeconds: 3, width: 320, height: 180, posterUrl: null, playlistUrl: null, createdAt: "2026-10-16T08:00:00Z", errorCode: null });

describe("video state helpers", () => {
  it("rounds the upload progress down to whole percent between 0 and 100", () => {
    expect(uploadPercent(0, 100)).toBe(0);
    expect(uploadPercent(1, 3)).toBe(33);
    expect(uploadPercent(99.9, 100)).toBe(99);
    expect(uploadPercent(100, 100)).toBe(100);
    expect(uploadPercent(5, 0)).toBe(0);
    expect(uploadPercent(500, 100)).toBe(100);
  });

  it("formats a duration as minutes and seconds", () => {
    expect(formatDuration(0)).toBe("0:00");
    expect(formatDuration(5.4)).toBe("0:05");
    expect(formatDuration(65)).toBe("1:05");
    expect(formatDuration(180)).toBe("3:00");
    expect(formatDuration(null)).toBe("");
    expect(formatDuration(Number.NaN)).toBe("");
  });

  it("polls while a video is being prepared", () => {
    expect(hasProcessing([video("ready"), video("processing")])).toBe(true);
    expect(hasProcessing([video("ready"), video("failed")])).toBe(false);
    expect(hasProcessing([])).toBe(false);
  });

  it("reads the state of a video, treating an unknown state as processing", () => {
    expect(videoStateOf(video("ready"))).toBe("ready");
    expect(videoStateOf({ ...video("ready"), playlistUrl: null })).toBe("ready");
    expect(videoStateOf({ ...video("processing"), state: "weird" as never })).toBe("processing");
  });

  it("describes an upload: idle, uploading, resuming and failed", () => {
    expect(describeUpload({ phase: "idle" })).toBe("idle");
    expect(describeUpload({ phase: "uploading", percent: 10, resuming: false })).toBe("uploading");
    expect(describeUpload({ phase: "uploading", percent: 10, resuming: true })).toBe("resuming");
    expect(describeUpload({ phase: "failed", error: new Error("x") })).toBe("failed");
  });
});
