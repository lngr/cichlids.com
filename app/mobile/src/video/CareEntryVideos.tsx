import { useEffect } from "react";
import { View } from "react-native";
import type { CareEntry } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useTheme } from "../theme";
import { VideoStatusRow } from "./VideoStatusRow";
import { VideoUploadControl } from "./VideoUploadControl";
import { hasProcessing } from "./videoState";

/** The most videos a care entry holds. */
export const MAX_CARE_VIDEOS = 2;
const POLL_MILLISECONDS = 3000;

/**
 * The videos of a care entry, served through signed paths, with the control to add one. The care
 * list is loaded again every few seconds while a video of the entry is being prepared.
 */
export function CareEntryVideos({ entry, reload, readOnly = false }: { entry: CareEntry; reload: () => void; readOnly?: boolean }) {
  const theme = useTheme();
  const processing = hasProcessing(entry.videos);
  useEffect(() => {
    if (!processing) return;
    const timer = setInterval(reload, POLL_MILLISECONDS);
    return () => clearInterval(timer);
  }, [processing, reload]);
  const tankId = Number(entry.tankId);
  const url = (path: string | null | undefined) => (path ? apiClient.videos.careVideoUrl(path) : null);
  return <View style={{ gap: theme.space[2] }}>
    {entry.videos.map(video => <VideoStatusRow key={String(video.id)} testID={`care-video-${video.id}`} video={video}
      posterUrl={url(video.posterUrl)} playlistUrl={url(video.playlistUrl)}
      onRemove={readOnly ? undefined : () => void apiClient.videos.removeFromCareEntry(tankId, Number(entry.id), Number(video.id)).then(reload, reload)} />)}
    {!readOnly && entry.videos.length < MAX_CARE_VIDEOS
      ? <VideoUploadControl testID={`care-video-add-${entry.id}`} target={{ target: "care_entry", tankId, careEntryId: Number(entry.id) }} onUploaded={reload} />
      : null}
  </View>;
}
