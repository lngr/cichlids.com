import { useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import type { Video } from "@cichlids/client-core";
import { minTouchTarget, useTheme } from "../theme";
import { VideoPlayer } from "./VideoPlayer";
import { formatDuration } from "./videoState";

/**
 * A ready video: its poster frame with a play button, replaced by the player when the member
 * presses play. The stream is not loaded before that. Urls are absolute.
 */
export function VideoCard({ video, posterUrl, playlistUrl, testID }: { video: Video; posterUrl: string | null; playlistUrl: string; testID?: string }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [playing, setPlaying] = useState(false);
  const width = Number(video.width) || 16;
  const height = Number(video.height) || 9;
  const label = t("video.preview", { duration: formatDuration(video.durationSeconds === null ? null : Number(video.durationSeconds)) });
  return <View testID={testID} style={[styles.frame, { aspectRatio: width / height, borderRadius: theme.radius.md, backgroundColor: theme.colors.placeholder }]}>
    {playing
      ? <VideoPlayer testID={testID ? `${testID}-player` : undefined} playlistUrl={playlistUrl} posterUrl={posterUrl} label={t("video.playerTitle")} autoPlay />
      : <>
        {posterUrl ? <Image testID={testID ? `${testID}-poster` : undefined} source={posterUrl} contentFit="cover" accessibilityLabel={label} style={StyleSheet.absoluteFill} /> : null}
        <View style={styles.center} pointerEvents="box-none">
          <Pressable testID={testID ? `${testID}-play` : undefined} accessibilityRole="button" accessibilityLabel={t("video.play")} onPress={() => setPlaying(true)}
            style={[styles.play, { backgroundColor: theme.colors.accent, borderRadius: theme.radius.pill }]}>
            <MaterialCommunityIcons name="play" size={30} color={theme.colors.onAccent} />
          </Pressable>
        </View>
      </>}
  </View>;
}

const styles = StyleSheet.create({
  frame: { width: "100%", maxHeight: 520, overflow: "hidden" },
  center: { position: "absolute", top: 0, bottom: 0, start: 0, end: 0, alignItems: "center", justifyContent: "center" },
  play: { width: minTouchTarget + 8, height: minTouchTarget + 8, alignItems: "center", justifyContent: "center" },
});
