import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { Video } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { VideoPickButtons } from "./VideoPickButtons";
import { useVideoUpload, type VideoTarget } from "./useVideoUpload";
import { describeUpload } from "./videoState";

/** The pick buttons of a target, the upload progress with a way to cancel, and the reason an upload failed. */
export function VideoUploadControl({ target, testID, onUploaded }: { target: VideoTarget; testID: string; onUploaded: (video: Video) => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const upload = useVideoUpload(target, onUploaded);
  const mode = describeUpload(upload.status);
  const busy = mode === "uploading" || mode === "resuming";
  const percent = upload.status.phase === "uploading" ? upload.status.percent : 0;
  return <View style={{ gap: theme.space[2] }}>
    <VideoPickButtons testID={testID} disabled={busy} onPicked={video => void upload.start(video)}
      onError={error => upload.fail(error)} />
    {busy ? <View style={{ gap: theme.space[2] }}>
      <View testID={`${testID}-progress`} accessibilityRole="progressbar" accessibilityLabel={t("video.uploading", { percent })}
        accessibilityValue={{ min: 0, max: 100, now: percent }}
        style={{ height: 8, borderRadius: theme.radius.pill, backgroundColor: theme.colors.accentWeak, overflow: "hidden" }}>
        <View style={{ height: 8, width: `${percent}%`, backgroundColor: theme.colors.accent }} />
      </View>
      <Text accessibilityLiveRegion="polite" style={[theme.type.meta, { color: theme.colors.muted }]}>
        {mode === "resuming" ? t("video.resuming") : t("video.uploading", { percent })}
      </Text>
      <Button testID={`${testID}-cancel`} variant="secondary" label={t("video.cancel")} onPress={upload.cancel} />
    </View> : null}
    {upload.status.phase === "failed" ? <Text testID={`${testID}-error`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>
      {translateApiError(upload.status.error, t, "video.pickError")}
    </Text> : null}
  </View>;
}
