import { ActivityIndicator, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { Video } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { VideoCard } from "./VideoCard";
import { videoStateOf } from "./videoState";

/**
 * One video of the owner: the player card when ready, a calm note while it is being prepared and a
 * calm note with the way out when preparing failed. Urls are absolute.
 */
export function VideoStatusRow({ video, posterUrl, playlistUrl, testID, onRemove }: {
  video: Video; posterUrl: string | null; playlistUrl: string | null; testID: string; onRemove: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const state = videoStateOf(video);
  return <View testID={testID} style={{ gap: theme.space[2] }}>
    {state === "ready" && playlistUrl ? <VideoCard testID={`${testID}-card`} video={video} posterUrl={posterUrl} playlistUrl={playlistUrl} /> : null}
    {state === "processing" ? <View testID={`${testID}-processing`} accessible accessibilityLiveRegion="polite" style={{ flexDirection: "row", alignItems: "center", gap: theme.space[2], minHeight: 48 }}>
      <ActivityIndicator color={theme.colors.accent} />
      <Text style={[theme.type.body, { color: theme.colors.fg, flexShrink: 1 }]}>{t("video.processing")}</Text>
    </View> : null}
    {state === "failed" ? <Text testID={`${testID}-failed`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.fg }]}>{t("video.failed")}</Text> : null}
    {state !== "processing" ? <Button testID={`${testID}-remove`} variant="secondary" label={t("video.remove")} onPress={onRemove} /> : null}
  </View>;
}
