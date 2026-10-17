import { useCallback, useEffect, useRef, useState } from "react";
import { Linking, Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { CameraView, useCameraPermissions } from "expo-camera";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import type { FishPage, LibraryItem, UploadFile } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { usePagedList } from "../hooks/usePagedList";
import { tileImage, tileLabel } from "../library/libraryView";
import { translateApiError } from "../lib/apiErrors";
import { cameraStage, previewUri, releasePreview, shotToUploadFile } from "../tanks/cameraShot";
import { localTime } from "../today/localDate";
import { useTheme } from "../theme";
import { PhotoPickButton } from "../upload/PhotoPickButton";
import { SheetFrame } from "./FishSheets";

type Mode = "choose" | "library" | "camera";

/**
 * The sheet that adds a photo or video to the page of a fish. The owner links a file of the own media
 * library, takes a photo with the camera or picks a photo from the device; the last two put the photo
 * into the library first.
 */
export function AddMediaSheet({ page, visible, onSaved, onCancel }: {
  page: FishPage; visible: boolean; onSaved: (page: FishPage) => void; onCancel: () => void;
}) {
  return <SheetFrame visible={visible} onClose={onCancel}><AddMedia page={page} onSaved={onSaved} onCancel={onCancel} /></SheetFrame>;
}

function AddMedia({ page, onSaved, onCancel }: { page: FishPage; onSaved: (page: FishPage) => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [mode, setMode] = useState<Mode>("choose");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const attach = useCallback(async (action: () => Promise<FishPage>) => {
    setBusy(true);
    setError(null);
    try {
      onSaved(await action());
    } catch (err) {
      setError(translateApiError(err, t, "fish.photoSheet.error"));
    } finally {
      setBusy(false);
    }
  }, [onSaved, t]);
  const linkItem = (item: LibraryItem) => attach(() => apiClient.fish.linkMedia(page.id, item.id, localTime()));
  const linkFile = (file: UploadFile) => attach(async () => {
    const item = await apiClient.library.uploadPhoto(file);
    return apiClient.fish.linkMedia(page.id, item.id, localTime());
  });
  const shown = new Set([...page.photos.map(photo => photo.mediaItemId), ...page.videos.map(video => Number(video.id))]);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("fish.photoSheet.title", { name: page.name })}</Text>
    {mode === "choose" ? <>
      <Button testID="fish-photo-library" label={t("fish.photoSheet.library")} disabled={busy} onPress={() => setMode("library")} />
      <Button testID="fish-photo-camera" label={t("fish.photoSheet.camera")} variant="secondary" disabled={busy} onPress={() => setMode("camera")} />
      <PhotoPickButton testID="fish-photo-upload" variant="secondary" label={busy ? t("fish.photoSheet.uploading") : t("fish.photoSheet.upload")} disabled={busy}
        onPicked={file => void linkFile(file)} onError={() => setError(t("library.pickError"))} />
    </> : null}
    {mode === "library" ? <LibraryChoice shown={shown} busy={busy} onChoose={item => void linkItem(item)} onBack={() => setMode("choose")} /> : null}
    {mode === "camera" ? <FishCamera name={page.name} busy={busy} onUse={file => void linkFile(file)} onBack={() => setMode("choose")} /> : null}
    {error ? <Text testID="fish-photo-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="fish-photo-cancel" variant="secondary" label={t("navigation.cancel")} disabled={busy} onPress={onCancel} />
  </>;
}

const TILE = 96;

function LibraryChoice({ shown, busy, onChoose, onBack }: { shown: Set<number>; busy: boolean; onChoose: (item: LibraryItem) => void; onBack: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const fetchPage = useCallback((offset: number, limit: number) => apiClient.library.list({ offset, limit }), []);
  const list = usePagedList(fetchPage, [], item => item.id);
  const items = list.items.filter(item => !shown.has(item.id));
  return <>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("fish.photoSheet.libraryTitle")}</Text>
    {list.loading ? <LoadingView /> : null}
    {list.error ? <ErrorView message={list.error} onRetry={list.reload} /> : null}
    {!list.loading && !list.error && items.length === 0 && !list.hasMore ? <Text testID="fish-library-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("fish.photoSheet.libraryEmpty")}</Text> : null}
    <View testID="fish-library-grid" style={styles.grid}>
      {items.map(item => {
        const picture = tileImage(item);
        return <Pressable key={item.id} testID={`fish-library-tile-${item.id}`} accessibilityRole="button" accessibilityLabel={tileLabel(item, t, i18n.language)} disabled={busy}
          onPress={() => onChoose(item)} style={[styles.tile, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md, opacity: busy ? 0.6 : 1 }]}>
          {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" /> : null}
          {item.kind === "video" ? <View style={[styles.play, { backgroundColor: theme.colors.surface }]}>
            <MaterialCommunityIcons name="play" size={18} color={theme.colors.fg} />
          </View> : null}
        </Pressable>;
      })}
    </View>
    {list.hasMore && !list.loading ? <Button testID="fish-library-more" variant="secondary" label={t("fish.photoSheet.more")} busy={list.loadingMore} onPress={list.loadMore} /> : null}
    <Button testID="fish-library-back" variant="secondary" label={t("navigation.back")} disabled={busy} onPress={onBack} />
  </>;
}

function FishCamera({ name, busy, onUse, onBack }: { name: string; busy: boolean; onUse: (file: UploadFile) => void; onBack: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [permission, requestPermission] = useCameraPermissions();
  const [requested, setRequested] = useState(false);
  const [problem, setProblem] = useState<"unavailable" | "capture" | null>(null);
  const [capturing, setCapturing] = useState(false);
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

  async function capture() {
    if (!camera.current || capturing) return;
    setCapturing(true);
    setProblem(null);
    try {
      const picture = await camera.current.takePictureAsync({ quality: 0.9, imageType: "jpg" });
      setShot(await shotToUploadFile(picture, Date.now(), "fish"));
    } catch {
      setProblem("capture");
    } finally {
      setCapturing(false);
    }
  }
  async function allow() {
    setRequested(true);
    await requestPermission();
  }

  if (shot) {
    return <>
      <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("timeLapseCamera.reviewTitle")}</Text>
      <View testID="fish-camera-review" style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
        {shotUri ? <Image source={shotUri} accessibilityLabel={t("fish.camera.reviewLabel", { name })} contentFit="cover" style={StyleSheet.absoluteFill} /> : null}
      </View>
      <Button testID="fish-camera-use" label={t(busy ? "timeLapseCamera.uploading" : "timeLapseCamera.use")} busy={busy} onPress={() => onUse(shot)} />
      <Button testID="fish-camera-retake" variant="secondary" label={t("timeLapseCamera.retake")} disabled={busy} onPress={() => setShot(null)} />
    </>;
  }
  return <>
    {stage === "checking" ? <LoadingView /> : null}
    {stage === "ready" && problem !== "unavailable" ? <>
      <View testID="fish-camera-viewfinder" style={[styles.frame, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]}>
        <CameraView ref={camera} facing="back" mode="picture" accessibilityLabel={t("timeLapseCamera.viewfinder")} style={StyleSheet.absoluteFill}
          onMountError={() => setProblem("unavailable")} />
      </View>
      <Button testID="fish-camera-capture" label={t("timeLapseCamera.capture")} busy={capturing} onPress={() => void capture()} />
      {problem === "capture" ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("timeLapseCamera.captureError")}</Text> : null}
    </> : null}
    {stage === "explain" ? <View testID="fish-camera-explain" style={{ gap: theme.space[2] }}>
      <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("timeLapseCamera.explainTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("fish.camera.explain", { name })}</Text>
      <Button testID="fish-camera-allow" label={t("timeLapseCamera.allow")} onPress={() => void allow()} />
      {requested ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("fish.camera.notAllowedYet")}</Text> : null}
    </View> : null}
    {stage === "denied" || (stage === "ready" && problem === "unavailable") ? <View testID="fish-camera-denied" style={{ gap: theme.space[2] }}>
      <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(stage === "denied" ? "timeLapseCamera.deniedTitle" : "timeLapseCamera.unavailableTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(stage !== "denied" ? "fish.camera.unavailable" : Platform.OS === "web" ? "fish.camera.deniedWeb" : "fish.camera.denied")}</Text>
      {stage === "denied" && Platform.OS !== "web" ? <Button testID="fish-camera-settings" label={t("timeLapseCamera.openSettings")} onPress={() => void Linking.openSettings()} /> : null}
    </View> : null}
    <Button testID="fish-camera-back" variant="secondary" label={t("navigation.back")} disabled={busy} onPress={onBack} />
  </>;
}

const styles = StyleSheet.create({
  grid: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  tile: { width: TILE, height: TILE, overflow: "hidden" },
  play: { position: "absolute", top: 4, end: 4, width: 26, height: 26, borderRadius: 13, alignItems: "center", justifyContent: "center" },
  frame: { width: "100%", aspectRatio: 3 / 4, overflow: "hidden" },
});
