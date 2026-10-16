import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Linking, Platform, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { CameraView, useCameraPermissions } from "expo-camera";
import { createActor } from "xstate";
import { useTranslation } from "react-i18next";
import { activeDecommissioning, proofCameraMachine, type DecommissionScope, type TankOperation, type UploadFile } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { reminderRefresh } from "../reminders/refreshSignal";
import { cameraStage, previewUri, releasePreview, shotToUploadFile } from "../tanks/cameraShot";
import { localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";

const SCOPES: readonly DecommissionScope[] = ["all_care", "water_change"];

/**
 * Decommissions an own tank or puts it back into operation. Decommissioning asks what pauses and then
 * takes a photo of the empty tank; putting the tank back takes a photo of the filled tank. Only a photo
 * taken with the camera of the app counts.
 */
export function DecommissionScreen({ tankId }: { tankId: number }) {
  const { status } = useAuth();
  const router = useMachineRouter();
  const { t } = useTranslation();
  const [operation, setOperation] = useState<TankOperation | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (status === "anonymous") router.replace("/me");
  }, [status, router]);
  useEffect(() => {
    let cancelled = false;
    setError(null);
    apiClient.tanks.operation(tankId, localTime())
      .then(value => { if (!cancelled) setOperation(value); })
      .catch(err => { if (!cancelled) setError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  }, [tankId, attempt, t]);
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!operation || status === "loading" || status === "anonymous") return <LoadingView />;
  return <Flow tankId={tankId} ending={activeDecommissioning(operation) !== null} />;
}

function Flow({ tankId, ending }: { tankId: number; ending: boolean }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [scope, setScope] = useState<DecommissionScope | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const text = { color: theme.colors.fg };
  if (!ending && !confirmed) {
    return <ScrollView testID="decommission-scope" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
      <View style={styles.page}>
        <Text accessibilityRole="header" style={[theme.type.h1, text]}>{t("decommission.start")}</Text>
        <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("decommission.intro")}</Text>
        <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("decommission.scopeTitle")}</Text>
        <View accessibilityRole="radiogroup" style={{ gap: theme.space[2] }}>
          {SCOPES.map(choice => {
            const selected = scope === choice;
            const key = choice === "all_care" ? "choiceAll" : "choiceWater";
            return <Pressable key={choice} testID={`decommission-scope-${choice}`} accessibilityRole="radio" accessibilityState={{ checked: selected }} aria-checked={selected}
              accessibilityLabel={t(`decommission.${key}`)} accessibilityHint={t(`decommission.${key}Hint`)} onPress={() => setScope(choice)}
              style={{ minHeight: minTouchTarget, gap: theme.space[1], padding: theme.space[3], borderRadius: theme.radius.md, borderWidth: selected ? 2 : 1,
                borderColor: selected ? theme.colors.accent : theme.colors.border, backgroundColor: theme.colors.surface }}>
              <Text style={[theme.type.bodyStrong, text]}>{t(`decommission.${key}`)}</Text>
              <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t(`decommission.${key}Hint`)}</Text>
            </Pressable>;
          })}
        </View>
        <Button testID="decommission-continue" label={t("decommission.start")} disabled={scope === null} onPress={() => setConfirmed(true)} />
      </View>
    </ScrollView>;
  }
  return <ProofFlow tankId={tankId} scope={ending ? null : scope!} />;
}

function ProofFlow({ tankId, scope }: { tankId: number; scope: DecommissionScope | null }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [actor] = useState(() => createActor(proofCameraMachine, {
    input: {
      upload: (file: UploadFile, capturedInApp: boolean) => scope === null
        ? apiClient.tanks.recommission(tankId, file, capturedInApp, localTime())
        : apiClient.tanks.decommission(tankId, file, { scope, capturedInApp }, localTime()),
    },
  }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const { context } = state;
  const done = state.status === "done";
  const leaving = useRef(false);
  useEffect(() => {
    if (!done || leaving.current) return;
    leaving.current = true;
    reminderRefresh.request();
    router.dismissTo(`/tanks/${tankId}`);
  }, [done, router, tankId]);

  const [permission, requestPermission] = useCameraPermissions();
  const [cameraProblem, setCameraProblem] = useState<"unavailable" | "capture" | null>(null);
  const [capturing, setCapturing] = useState(false);
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
  const filled = scope === null;
  const card = { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg };

  async function capture() {
    if (!camera.current || capturing) return;
    setCapturing(true);
    setCameraProblem(null);
    try {
      const picture = await camera.current.takePictureAsync({ quality: 0.9, imageType: "jpg" });
      actor.send({ type: "SHOT", file: await shotToUploadFile(picture), capturedInApp: true });
    } catch {
      setCameraProblem("capture");
    } finally {
      setCapturing(false);
    }
  }
  async function allow() {
    await requestPermission();
  }

  if (state.matches("framing")) {
    return <ScrollView testID="decommission-camera" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
      <View style={styles.page}>
        <Text accessibilityRole="header" style={[theme.type.h1, text]}>{t(filled ? "decommission.end" : "decommission.start")}</Text>
        <Text testID="decommission-hint" style={[theme.type.body, muted]}>{t(filled ? "decommission.filledHint" : "decommission.emptyHint")}</Text>
        {stage === "ready" && cameraProblem !== "unavailable" ? <>
          <View testID="decommission-viewfinder" style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
            <CameraView ref={camera} facing="back" mode="picture" accessibilityLabel={t("timeLapseCamera.viewfinder")} style={StyleSheet.absoluteFill}
              onMountError={() => setCameraProblem("unavailable")} />
          </View>
          <Button testID="decommission-capture" label={t("timeLapseCamera.capture")} busy={capturing} onPress={() => void capture()} />
          {cameraProblem === "capture" ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("timeLapseCamera.captureError")}</Text> : null}
        </> : <>
          {stage === "checking" ? <LoadingView /> : null}
          {stage === "explain" ? <View testID="decommission-explain" style={[styles.card, card]}>
            <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("decommission.explainTitle")}</Text>
            <Text style={[theme.type.body, text]}>{t("decommission.explain")}</Text>
            <Button testID="decommission-allow" label={t("timeLapseCamera.allow")} onPress={() => void allow()} />
          </View> : null}
          {stage === "denied" || (stage === "ready" && cameraProblem === "unavailable") ? <View testID="decommission-denied" style={[styles.card, card]}>
            <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t(stage === "denied" ? "timeLapseCamera.deniedTitle" : "timeLapseCamera.unavailableTitle")}</Text>
            <Text style={[theme.type.body, text]}>{t(stage !== "denied" ? "decommission.unavailable" : Platform.OS === "web" ? "decommission.deniedWeb" : "decommission.denied")}</Text>
            {stage === "denied" && Platform.OS !== "web" ? <Button testID="decommission-settings" label={t("timeLapseCamera.openSettings")} onPress={() => void Linking.openSettings()} /> : null}
          </View> : null}
        </>}
      </View>
    </ScrollView>;
  }

  const sending = state.matches("uploading");
  return <ScrollView testID="decommission-review" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
    <View style={styles.page}>
      <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("timeLapseCamera.reviewTitle")}</Text>
      <View style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
        {shotUri ? <Image source={shotUri} accessibilityLabel={t(filled ? "decommission.reviewFilled" : "decommission.reviewEmpty")} contentFit="cover" style={StyleSheet.absoluteFill} /> : null}
      </View>
      {context.error ? <Text accessibilityRole="alert" testID="decommission-error" style={[theme.type.body, { color: theme.colors.danger }]}>
        {translateApiError(context.error, t, "decommission.sendError")}</Text> : null}
      <Button testID="decommission-use" label={t(sending ? "timeLapseCamera.uploading" : "timeLapseCamera.use")} busy={sending} onPress={() => actor.send({ type: "USE" })} />
      <Button testID="decommission-retake" variant="secondary" label={t("timeLapseCamera.retake")} disabled={sending} onPress={() => actor.send({ type: "RETAKE" })} />
    </View>
  </ScrollView>;
}

const styles = StyleSheet.create({
  content: { padding: 16, paddingBottom: 32 },
  page: { width: "100%", maxWidth: 560, alignSelf: "center", gap: 16 },
  frame: { width: "100%", aspectRatio: 3 / 4, overflow: "hidden" },
  card: { borderWidth: 1, padding: 16, gap: 12 },
});
