import { useCallback, useEffect, useState } from "react";
import type { Video } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { hasProcessing } from "./videoState";

const POLL_MILLISECONDS = 3000;

/**
 * The videos of a tank in every state, for the owner; empty for everybody else. The list is
 * loaded again every few seconds while a video is being prepared.
 */
export function useTankVideos(tankId: number, enabled: boolean) {
  const [videos, setVideos] = useState<Video[]>([]);
  const reload = useCallback(async () => {
    try {
      setVideos(await apiClient.videos.listForTank(tankId));
    } catch {
      // The list keeps its last state; the next poll or reload tries again.
    }
  }, [tankId]);
  useEffect(() => {
    if (!enabled) { setVideos([]); return; }
    void reload();
  }, [enabled, reload]);
  useEffect(() => {
    if (!enabled || !hasProcessing(videos)) return;
    const timer = setInterval(() => void reload(), POLL_MILLISECONDS);
    return () => clearInterval(timer);
  }, [enabled, videos, reload]);
  return { videos, reload };
}
