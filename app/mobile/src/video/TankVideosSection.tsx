import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { Video } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useTheme } from "../theme";
import { VideoStatusRow } from "./VideoStatusRow";
import { VideoUploadControl } from "./VideoUploadControl";

/** The owner's videos of a tank with their states and the controls to add and remove videos. */
export function TankVideosSection({ tankId, videos, reload }: { tankId: number; videos: Video[]; reload: () => Promise<void> }) {
  const { t } = useTranslation();
  const theme = useTheme();
  return <View testID="tank-videos" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{t("video.title")}</Text>
    {videos.length === 0 ? <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("video.empty")}</Text> : null}
    {videos.map(video => <VideoStatusRow key={String(video.id)} testID={`tank-video-${video.id}`} video={video}
      posterUrl={video.posterUrl ?? null} playlistUrl={video.playlistUrl ?? null}
      onRemove={() => void apiClient.videos.removeFromTank(tankId, Number(video.id)).then(reload, reload)} />)}
    <VideoUploadControl testID="tank-video" target={{ target: "tank", tankId, careEntryId: null }} onUploaded={() => void reload()} />
  </View>;
}
