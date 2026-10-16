import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Linking, Platform, ScrollView, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { CameraView, useCameraPermissions } from "expo-camera";
import { createActor } from "xstate";
import { useTranslation } from "react-i18next";
import {
  latestSeriesPhotoUrl, overlayOpacity, timeLapseCameraMachine, type UploadFile,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { tankSeriesSignal } from "../lib/tankSeriesSignal";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { PhotoPickButton } from "../upload/PhotoPickButton";
import { cameraStage, previewUri, releasePreview, shotToUploadFile } from "./cameraShot";
import { OpacitySlider } from "./OpacitySlider";

/**
 * The guided time-lapse camera of an own tank: it loads the series, shows its newest photo over
 * the viewfinder and adds the photo the member takes or picks to the series.
 */
export function TimeLapseCameraScreen({ tankId }: { tankId: number }) {
  const { status } = useAuth();
  const router = useMachineRouter();
  const { t } = useTranslation();
  const [previous, setPrevious] = useState<{ url: string | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (status === "anonymous") router.replace("/me");
  }, [status, router]);
  useEffect(() => {
    let cancelled = false;
    setError(null);
    apiClient.tanks.timeLapse(tankId)
      .then(series => { if (!cancelled) setPrevious({ url: latestSeriesPhotoUrl(series) }); })
      .catch(err => { if (!cancelled) setError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  }, [tankId, attempt, t]);
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!previous || status === "loading") return <LoadingView />;
  return <CameraFlow tankId={tankId} previous={previous.url} />;
}

function CameraFlow({ tankId, previous }: { tankId: number; previous: string | null }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [actor] = useState(() => createActor(timeLapseCameraMachine, {
    input: { previous, upload: (file: UploadFile) => apiClient.tanks.addTimeLapsePhoto(tankId, file) },
  }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const { context } = state;
  const opacity = overlayOpacity(context);
  const done = state.status === "done";
  const leaving = useRef(false);
  useEffect(() => {
    if (!done || leaving.current) return;
    leaving.current = true;
    tankSeriesSignal.notifyPublished();
    router.dismissTo(`/tanks/${tankId}`);
  }, [done, router, tankId]);

  const [permission, requestPermission] = useCameraPermissions();
  const [requested, setRequested] = useState(false);
  const [cameraProblem, setCameraProblem] = useState<"unavailable" | "capture" | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [pickProblem, setPickProblem] = useState(false);
  const camera = useRef<CameraView>(null);
  const stage = cameraStage(permission);
  const shot = context.shot;
  const [shotUri, setShotUri] = useState<string | null>(null);
  useEffect(() => {
    if (!shot) { setShotUri(null); return; }
    const uri = previewUri(shot);
    setShotUri(uri);
    return () => releasePreview(uri);
  }, [shot]);

  const text = { color: theme.colors.fg };
  const muted = { color: theme.colors.muted };
  const percent = Math.round(context.opacity * 100);
  const library = <PhotoPickButton testID="time-lapse-pick" variant="secondary" label={t("timeLapseCamera.library")}
    onPicked={file => { setPickProblem(false); actor.send({ type: "SHOT", file }); }} onError={() => setPickProblem(true)} />;

  async function capture() {
    if (!camera.current || capturing) return;
    setCapturing(true);
    setCameraProblem(null);
    try {
      const picture = await camera.current.takePictureAsync({ quality: 0.9, imageType: "jpg" });
      actor.send({ type: "SHOT", file: await shotToUploadFile(picture) });
    } catch {
      setCameraProblem("capture");
    } finally {
      setCapturing(false);
    }
  }
  async function allow() {
    setRequested(true);
    await requestPermission();
  }

  const frame = (image: string | null, label: string) => <View testID="time-lapse-frame" style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
    {image ? <Image source={image} accessibilityLabel={label} contentFit="cover" style={StyleSheet.absoluteFill} /> : null}
    {previous && opacity > 0 ? <View testID="time-lapse-overlay" pointerEvents="none" style={[StyleSheet.absoluteFill, { opacity }]}>
      <Image source={previous} accessibilityLabel={t("timeLapseCamera.overlayLabel")} contentFit="cover" style={StyleSheet.absoluteFill} />
    </View> : null}
  </View>;

  const overlayControls = previous ? <View style={{ gap: theme.space[2] }}>
    <Button testID="time-lapse-overlay-toggle" variant="secondary"
      label={t(context.overlayVisible ? "timeLapseCamera.hideOverlay" : "timeLapseCamera.showOverlay")}
      onPress={() => actor.send({ type: "TOGGLE_OVERLAY" })} />
    {context.overlayVisible ? <View style={{ gap: theme.space[1] }}>
      <Text style={[theme.type.meta, muted]}>{t("timeLapseCamera.opacity")}: {t("timeLapseCamera.percent", { percent })}</Text>
      <OpacitySlider testID="time-lapse-opacity" value={context.opacity} onChange={value => actor.send({ type: "SET_OPACITY", value })}
        label={t("timeLapseCamera.opacity")} valueText={t("timeLapseCamera.percent", { percent })} />
    </View> : null}
  </View> : <Text testID="time-lapse-no-previous" style={[theme.type.body, muted]}>{t("timeLapseCamera.noPrevious")}</Text>;

  if (state.matches("framing")) {
    return <ScrollView testID="time-lapse-camera" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
      <View style={styles.page}>
        {stage === "ready" && cameraProblem !== "unavailable" ? <>
          <View testID="time-lapse-viewfinder" style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
            <CameraView ref={camera} facing="back" mode="picture" accessibilityLabel={t("timeLapseCamera.viewfinder")} style={StyleSheet.absoluteFill}
              onMountError={() => setCameraProblem("unavailable")} />
            {previous && opacity > 0 ? <View testID="time-lapse-overlay" pointerEvents="none" style={[StyleSheet.absoluteFill, { opacity }]}>
              <Image source={previous} accessibilityLabel={t("timeLapseCamera.overlayLabel")} contentFit="cover" style={StyleSheet.absoluteFill} />
            </View> : null}
          </View>
          {overlayControls}
          <Button testID="time-lapse-capture" label={t("timeLapseCamera.capture")} busy={capturing} onPress={() => void capture()} />
          {cameraProblem === "capture" ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("timeLapseCamera.captureError")}</Text> : null}
          {library}
        </> : <>
          {stage === "checking" ? <LoadingView /> : null}
          {stage === "explain" ? <View testID="time-lapse-explain" style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
            <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("timeLapseCamera.explainTitle")}</Text>
            <Text style={[theme.type.body, text]}>{t("timeLapseCamera.explain")}</Text>
            <Button testID="time-lapse-allow" label={t("timeLapseCamera.allow")} onPress={() => void allow()} />
          </View> : null}
          {stage === "denied" || (stage === "ready" && cameraProblem === "unavailable") ? <View testID="time-lapse-denied" style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
            <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t(stage === "denied" ? "timeLapseCamera.deniedTitle" : "timeLapseCamera.unavailableTitle")}</Text>
            <Text style={[theme.type.body, text]}>{t(stage !== "denied" ? "timeLapseCamera.unavailable" : Platform.OS === "web" ? "timeLapseCamera.deniedWeb" : "timeLapseCamera.denied")}</Text>
            {stage === "denied" && Platform.OS !== "web" ? <Button testID="time-lapse-settings" label={t("timeLapseCamera.openSettings")} onPress={() => void Linking.openSettings()} /> : null}
          </View> : null}
          {stage !== "checking" ? <>
            {requested && stage === "explain" ? <Text style={[theme.type.body, muted]}>{t("timeLapseCamera.notAllowedYet")}</Text> : null}
            {library}
          </> : null}
          {pickProblem ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("upload.pickError")}</Text> : null}
        </>}
      </View>
    </ScrollView>;
  }

  const uploading = state.matches("uploading");
  return <ScrollView testID="time-lapse-review" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
    <View style={styles.page}>
      <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("timeLapseCamera.reviewTitle")}</Text>
      {frame(shotUri, t("timeLapseCamera.reviewLabel"))}
      {overlayControls}
      {context.error ? <Text accessibilityRole="alert" testID="time-lapse-error" style={[theme.type.body, { color: theme.colors.danger }]}>
        {translateApiError(context.error, t, "timeLapseCamera.uploadError")}</Text> : null}
      <Button testID="time-lapse-use" label={t(uploading ? "timeLapseCamera.uploading" : "timeLapseCamera.use")} busy={uploading} onPress={() => actor.send({ type: "USE" })} />
      <Button testID="time-lapse-retake" variant="secondary" label={t("timeLapseCamera.retake")} disabled={uploading} onPress={() => actor.send({ type: "RETAKE" })} />
    </View>
  </ScrollView>;
}

const styles = StyleSheet.create({
  content: { padding: 16, paddingBottom: 32 },
  page: { width: "100%", maxWidth: 560, alignSelf: "center", gap: 16 },
  frame: { width: "100%", aspectRatio: 3 / 4, overflow: "hidden" },
  card: { borderWidth: 1, padding: 16, gap: 12 },
});
