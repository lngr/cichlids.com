import { useEffect, useRef, useState } from "react";
import { Linking, Platform, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { CameraView, useCameraPermissions } from "expo-camera";
import { useTranslation } from "react-i18next";
import type { UploadFile } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { LoadingView } from "../components/StatusView";
import { useTheme } from "../theme";
import { cameraStage, previewUri, releasePreview, shotToUploadFile } from "../tanks/cameraShot";

const OVERLAY_OPACITY = 0.4;

/**
 * The camera step of the guided water change. Only the camera of the app delivers a photo; the
 * overlay shows the drained photo of the previous water change for the same framing. A photo is
 * optional, so the step always offers a way on.
 */
export function WaterChangeCamera({ overlay, uploading, error, onUse, onSkip }: {
  overlay: string | null;
  uploading: boolean;
  error: string | null;
  onUse: (file: UploadFile) => void;
  onSkip: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [permission, requestPermission] = useCameraPermissions();
  const [requested, setRequested] = useState(false);
  const [problem, setProblem] = useState<"unavailable" | "capture" | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [overlayVisible, setOverlayVisible] = useState(true);
  const [shot, setShot] = useState<UploadFile | null>(null);
  const [shotUri, setShotUri] = useState<string | null>(null);
  const camera = useRef<CameraView>(null);
  const stage = cameraStage(permission);
  useEffect(() => {
    if (!shot) { setShotUri(null); return; }
    const uri = previewUri(shot);
    setShotUri(uri);
    return () => releasePreview(uri);
  }, [shot]);

  const muted = { color: theme.colors.muted };
  const frameStyle = [styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }];
  const overlayView = overlay && overlayVisible
    ? <View testID="water-change-overlay" pointerEvents="none" style={[StyleSheet.absoluteFill, { opacity: OVERLAY_OPACITY }]}>
      <Image source={overlay} accessibilityLabel={t("waterChange.camera.overlayLabel")} contentFit="cover" style={StyleSheet.absoluteFill} />
    </View> : null;
  const skip = <Button testID="water-change-photo-skip" variant="secondary" label={t("waterChange.camera.skip")} disabled={uploading} onPress={onSkip} />;

  async function capture() {
    if (!camera.current || capturing) return;
    setCapturing(true);
    setProblem(null);
    try {
      const picture = await camera.current.takePictureAsync({ quality: 0.9, imageType: "jpg" });
      setShot(await shotToUploadFile(picture));
    } catch {
      setProblem("capture");
    } finally {
      setCapturing(false);
    }
  }

  if (shot) {
    return <View testID="water-change-review" style={{ gap: theme.space[3] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("waterChange.camera.reviewTitle")}</Text>
      <View style={frameStyle}>
        {shotUri ? <Image source={shotUri} accessibilityLabel={t("waterChange.camera.reviewLabel")} contentFit="cover" style={StyleSheet.absoluteFill} /> : null}
        {overlayView}
      </View>
      {error ? <Text accessibilityRole="alert" testID="water-change-photo-error" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      <Button testID="water-change-use" label={t(uploading ? "waterChange.camera.uploading" : "waterChange.camera.use")} busy={uploading} onPress={() => onUse(shot)} />
      <Button testID="water-change-retake" variant="secondary" label={t("waterChange.camera.retake")} disabled={uploading} onPress={() => setShot(null)} />
      {skip}
    </View>;
  }

  if (stage === "ready" && problem !== "unavailable") {
    return <View style={{ gap: theme.space[3] }}>
      <View testID="water-change-viewfinder" style={frameStyle}>
        <CameraView ref={camera} facing="back" mode="picture" accessibilityLabel={t("waterChange.camera.viewfinder")} style={StyleSheet.absoluteFill}
          onMountError={() => setProblem("unavailable")} />
        {overlayView}
      </View>
      {overlay ? <Button testID="water-change-overlay-toggle" variant="secondary"
        label={t(overlayVisible ? "waterChange.camera.hideOverlay" : "waterChange.camera.showOverlay")} onPress={() => setOverlayVisible(value => !value)} /> : null}
      <Button testID="water-change-capture" label={t("waterChange.camera.capture")} busy={capturing} onPress={() => void capture()} />
      {problem === "capture" ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("waterChange.camera.captureError")}</Text> : null}
      {skip}
    </View>;
  }

  const denied = stage === "denied" || problem === "unavailable";
  return <View style={{ gap: theme.space[3] }}>
    {stage === "checking" ? <LoadingView /> : null}
    {stage === "explain" ? <View testID="water-change-explain" style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("waterChange.camera.explainTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("waterChange.camera.explain")}</Text>
      <Button testID="water-change-allow" label={t("waterChange.camera.allow")} onPress={() => { setRequested(true); void requestPermission(); }} />
    </View> : null}
    {denied ? <View testID="water-change-denied" style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(stage === "denied" ? "waterChange.camera.deniedTitle" : "waterChange.camera.unavailableTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>
        {t(stage !== "denied" ? "waterChange.camera.unavailable" : Platform.OS === "web" ? "waterChange.camera.deniedWeb" : "waterChange.camera.denied")}
      </Text>
      {stage === "denied" && Platform.OS !== "web" ? <Button testID="water-change-settings" label={t("waterChange.camera.openSettings")} onPress={() => void Linking.openSettings()} /> : null}
    </View> : null}
    {requested && stage === "explain" ? <Text style={[theme.type.body, muted]}>{t("waterChange.camera.notAllowedYet")}</Text> : null}
    {stage !== "checking" ? skip : null}
  </View>;
}

const styles = StyleSheet.create({
  frame: { width: "100%", aspectRatio: 3 / 4, overflow: "hidden" },
  card: { borderWidth: 1, padding: 16, gap: 12 },
});
