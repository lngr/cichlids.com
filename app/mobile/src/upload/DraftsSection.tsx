import { useCallback, useRef, useState, type ReactNode } from "react";
import { ActivityIndicator, Alert, Platform, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useFocusEffect, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import type { Draft } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { formatDate } from "../lib/format";
import { minTouchTarget, useTheme } from "../theme";
import { draftThumbnailUri, draftTitle } from "./drafts";

const THUMBNAIL_SIZE = 64;

/**
 * The signed-in user's unpublished drafts, reloaded whenever the screen gains focus. Opening a
 * draft resumes it on the upload screen; deleting one discards it on the server, after a
 * confirmation on native platforms.
 */
export function DraftsSection() {
  const theme = useTheme();
  const router = useRouter();
  const { t, i18n } = useTranslation();

  const [drafts, setDrafts] = useState<Draft[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [deleting, setDeleting] = useState<ReadonlySet<string>>(new Set());
  const [deleteFailed, setDeleteFailed] = useState(false);
  // Blocks a second open until the screen regains focus.
  const opening = useRef(false);

  const load = useCallback(() => {
    let cancelled = false;
    setLoadFailed(false);
    apiClient.me
      .drafts()
      .then((result) => !cancelled && setDrafts(result))
      .catch(() => !cancelled && setLoadFailed(true));
    return () => {
      cancelled = true;
    };
  }, []);

  useFocusEffect(
    useCallback(() => {
      opening.current = false;
      return load();
    }, [load]),
  );

  const open = (draft: Draft) => {
    if (opening.current) return;
    opening.current = true;
    router.push(`/upload?draft=${encodeURIComponent(String(draft.id))}`);
  };

  const remove = async (draft: Draft) => {
    const id = String(draft.id);
    if (deleting.has(id)) return;
    setDeleteFailed(false);
    setDeleting((current) => new Set(current).add(id));
    try {
      await apiClient.posts.discard(Number(draft.id));
      setDrafts((current) => current?.filter((candidate) => String(candidate.id) !== id) ?? current);
    } catch {
      setDeleteFailed(true);
    } finally {
      setDeleting((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
    }
  };

  const confirmRemove = (draft: Draft) => {
    // Alert.alert does nothing on react-native-web, so the web deletes right away.
    if (Platform.OS === "web") {
      void remove(draft);
      return;
    }
    Alert.alert(t("me.drafts.deleteConfirmTitle"), t("me.drafts.deleteConfirmText"), [
      { text: t("me.drafts.cancel"), style: "cancel" },
      { text: t("me.drafts.delete"), style: "destructive", onPress: () => void remove(draft) },
    ]);
  };

  let body: ReactNode;
  if (drafts === null && loadFailed) {
    body = (
      <View style={{ gap: theme.space[2] }}>
        <Text style={[theme.type.body, { color: theme.colors.danger }]}>{t("me.drafts.loadError")}</Text>
        <Pressable accessibilityRole="button" onPress={load} style={styles.textButton}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.accent }]}>{t("common.retry")}</Text>
        </Pressable>
      </View>
    );
  } else if (drafts === null) {
    body = (
      <View style={[styles.row, { gap: theme.space[2] }]}>
        <ActivityIndicator color={theme.colors.accent} />
        <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("me.drafts.loading")}</Text>
      </View>
    );
  } else if (drafts.length === 0) {
    body = (
      <Text testID="drafts-empty" style={[theme.type.body, { color: theme.colors.muted }]}>
        {t("me.drafts.empty")}
      </Text>
    );
  } else {
    body = (
      <View style={{ gap: theme.space[2] }}>
        {drafts.map((draft) => {
          const id = String(draft.id);
          const date = formatDate(draft.createdAt, i18n.language);
          const busy = deleting.has(id);
          return (
            <View
              key={id}
              testID="draft-item"
              style={[
                styles.row,
                {
                  gap: theme.space[2],
                  backgroundColor: theme.colors.surface,
                  borderColor: theme.colors.border,
                  borderRadius: theme.radius.md,
                  padding: theme.space[2],
                },
              ]}
            >
              <Pressable
                testID="draft-open"
                accessibilityRole="button"
                accessibilityLabel={t("me.drafts.openLabel", { date })}
                onPress={() => open(draft)}
                disabled={busy}
                style={({ pressed }) => [styles.row, styles.open, { gap: theme.space[3], opacity: pressed ? 0.85 : 1 }]}
              >
                <Image
                  source={draftThumbnailUri(draft)}
                  style={[styles.thumbnail, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}
                  contentFit="cover"
                />
                <View style={styles.fill}>
                  <Text testID="draft-title" numberOfLines={2} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>
                    {draftTitle(draft) ?? t("me.drafts.untitled")}
                  </Text>
                  <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("me.drafts.created", { date })}</Text>
                </View>
              </Pressable>
              <Pressable
                testID="draft-delete"
                accessibilityRole="button"
                accessibilityLabel={t("me.drafts.delete")}
                accessibilityState={{ disabled: busy, busy }}
                onPress={() => confirmRemove(draft)}
                disabled={busy}
                style={({ pressed }) => [styles.iconButton, { opacity: busy ? 0.6 : pressed ? 0.85 : 1 }]}
              >
                {busy ? (
                  <ActivityIndicator color={theme.colors.muted} />
                ) : (
                  <MaterialCommunityIcons name="trash-can-outline" size={24} color={theme.colors.muted} />
                )}
              </Pressable>
            </View>
          );
        })}
      </View>
    );
  }

  return (
    <View testID="drafts-section" style={{ gap: theme.space[3] }}>
      <Text style={[theme.type.h2, { color: theme.colors.fg }]}>{t("me.drafts.title")}</Text>
      {deleteFailed ? <Text style={[theme.type.body, { color: theme.colors.danger }]}>{t("me.drafts.deleteError")}</Text> : null}
      {body}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
  },
  open: {
    flex: 1,
    minHeight: minTouchTarget,
  },
  fill: {
    flex: 1,
  },
  thumbnail: {
    width: THUMBNAIL_SIZE,
    height: THUMBNAIL_SIZE,
  },
  iconButton: {
    width: minTouchTarget,
    height: minTouchTarget,
    alignItems: "center",
    justifyContent: "center",
  },
  textButton: {
    minHeight: minTouchTarget,
    justifyContent: "center",
    alignSelf: "flex-start",
  },
});
