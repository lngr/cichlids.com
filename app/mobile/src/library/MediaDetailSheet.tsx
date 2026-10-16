import { useEffect, useState } from "react";
import { ActivityIndicator, Modal, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { LibraryDetail, LibraryItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { formatDate } from "../lib/format";
import { localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { describePlace, formatClock, previewImage } from "./libraryView";

/**
 * A sheet with one file of the media library: its picture, when it came in, the places that show it and
 * the deletion. Deleting asks first and names the places the file leaves; the confirmation is part of
 * the sheet, so it works the same on every platform.
 */
export function MediaDetailSheet({ item, onClose, onDeleted }: {
  item: LibraryItem | null;
  onClose: () => void;
  onDeleted: () => void;
}) {
  return <Modal visible={item !== null} transparent animationType="slide" onRequestClose={onClose}>
    <View style={styles.backdrop}>
      {item ? <SheetContent key={item.id} item={item} onClose={onClose} onDeleted={onDeleted} /> : null}
    </View>
  </Modal>;
}

function SheetContent({ item, onClose, onDeleted }: { item: LibraryItem; onClose: () => void; onDeleted: () => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const [detail, setDetail] = useState<LibraryDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    apiClient.library.get(item.id, localTime())
      .then((result) => !cancelled && setDetail(result))
      .catch((error) => !cancelled && setLoadError(translateApiError(error, t, "library.detailError")));
    return () => {
      cancelled = true;
    };
  }, [item.id, attempt, t]);

  const shown = detail?.item ?? item;
  const picture = previewImage(shown);
  const places = detail?.places ?? [];
  const placeLines = places.map((place) => describePlace(place, t, i18n.language));
  const streakLines = (detail?.keepsStreak ?? []).map((week) => t("library.streakWarning", { week: formatDate(week.weekStart, i18n.language), tank: week.tankTitle }));
  const ratio = shown.width && shown.height ? shown.width / shown.height : 4 / 3;

  const remove = async () => {
    if (busy) return;
    setBusy(true);
    setDeleteError(null);
    try {
      await apiClient.library.remove(item.id);
      onDeleted();
    } catch (error) {
      setDeleteError(translateApiError(error, t, "library.deleteError"));
      setBusy(false);
    }
  };

  const title = t(`library.kind.${shown.kind}`);
  return <View style={[styles.sheet, { backgroundColor: theme.colors.bg, borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet }]}>
    <ScrollView testID="library-detail" contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3] }} keyboardShouldPersistTaps="handled">
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{title}</Text>
      <View style={[styles.preview, { aspectRatio: ratio, backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}>
        {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="contain" accessibilityLabel={title} /> : null}
      </View>
      <Text testID="library-detail-meta" style={[theme.type.meta, { color: theme.colors.muted }]}>
        {[
          t("library.uploaded", { date: formatDate(shown.createdAt, i18n.language) }),
          shown.video?.durationSeconds ? formatClock(Number(shown.video.durationSeconds)) : null,
        ].filter(Boolean).join(" · ")}
      </Text>

      {loadError ? <View style={{ gap: theme.space[2] }}>
        <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{loadError}</Text>
        <Button testID="library-detail-retry" variant="secondary" label={t("common.retry")} onPress={() => setAttempt((n) => n + 1)} />
      </View> : null}
      {!detail && !loadError ? <ActivityIndicator color={theme.colors.accent} /> : null}

      {detail && !confirming ? <View style={{ gap: theme.space[2] }}>
        <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("library.placesTitle")}</Text>
        {placeLines.length === 0
          ? <Text testID="library-places-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("library.placesEmpty")}</Text>
          : placeLines.map((line, index) => <Text key={index} testID="library-place" style={[theme.type.body, { color: theme.colors.fg }]}>{line}</Text>)}
      </View> : null}

      {detail && !confirming ? streakLines.map((line, index) => <Text key={index} testID="library-streak-note" style={[theme.type.body, { color: theme.colors.warn }]}>{line}</Text>) : null}

      {detail && !detail.canDelete ? <Text testID="library-proof-note" style={[theme.type.body, { color: theme.colors.muted }]}>{t("library.proofNote")}</Text> : null}

      {confirming && detail ? <View testID="library-confirm" style={[styles.confirm, { borderColor: theme.colors.danger, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md, padding: theme.space[4], gap: theme.space[2] }]}>
        <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("library.confirmTitle")}</Text>
        <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(placeLines.length === 0 ? "library.confirmNoPlaces" : "library.confirmPlaces")}</Text>
        {streakLines.map((line, index) => <Text key={index} testID="library-confirm-streak" accessibilityRole="alert" style={[theme.type.bodyStrong, { color: theme.colors.danger }]}>{line}</Text>)}
        {placeLines.map((line, index) => <Text key={index} testID="library-confirm-place" style={[theme.type.body, { color: theme.colors.fg }]}>{`• ${line}`}</Text>)}
        {deleteError ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{deleteError}</Text> : null}
        <DangerButton testID="library-delete-confirm" label={t("library.deleteConfirm")} busy={busy} onPress={() => void remove()} />
        <Button testID="library-delete-cancel" variant="secondary" label={t("library.deleteCancel")} disabled={busy} onPress={() => setConfirming(false)} />
      </View> : null}

      {detail?.canDelete && !confirming ? <DangerButton testID="library-delete" label={t("library.delete")} busy={false} onPress={() => setConfirming(true)} /> : null}
      {!confirming ? <Button testID="library-detail-close" variant="secondary" label={t("library.close")} onPress={onClose} /> : null}
    </ScrollView>
  </View>;
}

function DangerButton({ label, onPress, busy, testID }: { label: string; onPress: () => void; busy: boolean; testID: string }) {
  const theme = useTheme();
  return <Pressable testID={testID} accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ busy, disabled: busy }} disabled={busy} onPress={onPress}
    style={({ pressed }) => [styles.danger, { borderColor: theme.colors.danger, backgroundColor: theme.colors.dangerWeak, borderRadius: theme.radius.md, opacity: busy ? 0.6 : pressed ? 0.85 : 1 }]}>
    {busy ? <ActivityIndicator color={theme.colors.danger} /> : <Text style={[theme.type.bodyStrong, { color: theme.colors.danger }]}>{label}</Text>}
  </Pressable>;
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.45)" },
  sheet: { maxHeight: "92%", width: "100%", maxWidth: 640, alignSelf: "center" },
  preview: { width: "100%", maxHeight: 360, overflow: "hidden" },
  confirm: { borderWidth: 1 },
  danger: { minHeight: minTouchTarget, borderWidth: 1, alignItems: "center", justifyContent: "center", alignSelf: "stretch", paddingHorizontal: 16 },
});
