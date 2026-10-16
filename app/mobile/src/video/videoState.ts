import type { Video } from "@cichlids/client-core";

/** Whole percent of an upload, rounded down and kept between 0 and 100. */
export function uploadPercent(uploaded: number, total: number): number {
  if (!(total > 0)) return 0;
  return Math.min(100, Math.max(0, Math.floor((uploaded / total) * 100)));
}

/** A duration as minutes and seconds, such as 1:05; empty without a valid duration. */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return "";
  const whole = Math.floor(seconds);
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}

/** Whether any video is still being prepared, so a list refreshes until it is done. */
export const hasProcessing = (videos: readonly Video[]): boolean => videos.some(video => videoStateOf(video) === "processing");

/** The state of a video; a state the app does not know counts as processing. */
export function videoStateOf(video: Video): "processing" | "ready" | "failed" {
  return video.state === "ready" || video.state === "failed" ? video.state : "processing";
}

export type UploadStatus =
  | { phase: "idle" }
  | { phase: "uploading"; percent: number; resuming: boolean }
  | { phase: "failed"; error: unknown };

/** What the upload control shows. */
export function describeUpload(status: UploadStatus): "idle" | "uploading" | "resuming" | "failed" {
  if (status.phase === "uploading") return status.resuming ? "resuming" : "uploading";
  return status.phase;
}
