import { VideoView, useVideoPlayer } from "expo-video";
import { StyleSheet } from "react-native";

export interface VideoPlayerProps {
  /** Absolute URL of the HLS master playlist. */
  playlistUrl: string;
  posterUrl: string | null;
  label: string;
  testID?: string;
  /** Starts playing as soon as the player is ready. */
  autoPlay?: boolean;
}

/** Plays an HLS stream with the native player and its system controls. */
export function VideoPlayer({ playlistUrl, label, testID, autoPlay }: VideoPlayerProps) {
  const player = useVideoPlayer({ uri: playlistUrl, contentType: "hls" }, instance => {
    if (autoPlay) instance.play();
  });
  return <VideoView testID={testID} player={player} nativeControls allowsPictureInPicture
    accessible accessibilityLabel={label} contentFit="contain" style={styles.view} />;
}

const styles = StyleSheet.create({ view: { width: "100%", height: "100%", backgroundColor: "#000" } });
