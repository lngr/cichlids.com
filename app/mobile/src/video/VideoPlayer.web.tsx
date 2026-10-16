import { createElement, useEffect, useRef, useState } from "react";
import { Text } from "react-native";
import Hls from "hls.js";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import type { VideoPlayerProps } from "./VideoPlayer";

/**
 * Plays an HLS stream in a video element with the browser's own controls. Browsers with native
 * HLS (Safari) load the playlist directly; the others play it through hls.js.
 */
export function VideoPlayer({ playlistUrl, posterUrl, label, testID, autoPlay }: VideoPlayerProps) {
  const { t } = useTranslation();
  const theme = useTheme();
  const ref = useRef<HTMLVideoElement>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    setFailed(false);
    const start = () => { if (autoPlay) void element.play().catch(() => undefined); };
    if (element.canPlayType("application/vnd.apple.mpegurl")) {
      element.src = playlistUrl;
      start();
      return;
    }
    if (!Hls.isSupported()) {
      setFailed(true);
      return;
    }
    const hls = new Hls();
    hls.on(Hls.Events.MANIFEST_PARSED, start);
    hls.on(Hls.Events.ERROR, (_event, data) => { if (data.fatal) setFailed(true); });
    hls.loadSource(playlistUrl);
    hls.attachMedia(element);
    return () => hls.destroy();
  }, [playlistUrl, autoPlay]);
  if (failed) return <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("video.unsupported")}</Text>;
  return createElement("video", {
    ref, controls: true, playsInline: true, poster: posterUrl ?? undefined, "aria-label": label, "data-testid": testID,
    style: { width: "100%", height: "100%", backgroundColor: "#000" },
  });
}
