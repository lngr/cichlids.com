import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, uploadVideo, type CreateVideoUploadRequest, type Video } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import type { PickedVideo } from "./pickedVideo";
import { uploadPercent, type UploadStatus } from "./videoState";

export type VideoTarget = Pick<CreateVideoUploadRequest, "target" | "tankId" | "careEntryId">;

/**
 * Uploads a picked video to a tank or a care entry with progress. A lost connection shows the
 * resuming state while the upload carries on from the offset the server holds; a refusal ends in the
 * failed state with the error.
 */
export function useVideoUpload(target: VideoTarget, onUploaded: (video: Video) => void) {
  const [status, setStatus] = useState<UploadStatus>({ phase: "idle" });
  const controller = useRef<AbortController | null>(null);
  const mounted = useRef(true);
  useEffect(() => () => { mounted.current = false; controller.current?.abort(); }, []);
  const start = useCallback(async (video: PickedVideo) => {
    const abort = new AbortController();
    controller.current = abort;
    let resuming = false;
    let percent = 0;
    const show = () => mounted.current && setStatus({ phase: "uploading", percent, resuming });
    const transport = {
      ...apiClient.videos.uploadTransport,
      async append(id: number, offset: number, chunk: Blob) {
        try {
          const next = await apiClient.videos.uploadTransport.append(id, offset, chunk);
          if (resuming) { resuming = false; show(); }
          return next;
        } catch (error) {
          if (error instanceof ApiError && error.status === 0) { resuming = true; show(); }
          throw error;
        }
      },
    };
    show();
    try {
      const uploaded = await uploadVideo(transport, video, { ...target, size: video.size, fileName: video.fileName }, {
        signal: abort.signal,
        onProgress: (done, total) => { percent = uploadPercent(done, total); show(); },
      });
      if (mounted.current) setStatus({ phase: "idle" });
      onUploaded(uploaded);
    } catch (error) {
      if (!mounted.current || (error as { name?: string }).name === "AbortError") return;
      setStatus({ phase: "failed", error });
    }
  }, [target.target, target.tankId, target.careEntryId, onUploaded]);
  const cancel = useCallback(() => { controller.current?.abort(); if (mounted.current) setStatus({ phase: "idle" }); }, []);
  const fail = useCallback((error: unknown) => setStatus({ phase: "failed", error }), []);
  return { status, start, cancel, fail };
}
