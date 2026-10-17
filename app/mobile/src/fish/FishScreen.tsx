import { useEffect, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import { useNavigation } from "expo-router";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { latestGrowth, type FishPage, type FishTimelineItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { formatLocalDate } from "../streaks/streakText";
import { localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { VideoCard } from "../video/VideoCard";
import { videoStateOf } from "../video/videoState";
import { DeathSheet, GrowthSheet, MoveSheet, SpawningSheet } from "./FishSheets";
import { AddMediaSheet } from "./FishMediaSheets";
import { deletableItem, lengthText, memorialText, tankText, timelineLabel, timelineText } from "./fishText";
import { useFishPage } from "./useFishPage";

type Sheet = "photo" | "growth" | "spawning" | "move" | "death";

function iconOf(kind: FishTimelineItem["kind"]): keyof typeof MaterialCommunityIcons.glyphMap {
  switch (kind) {
    case "moved_in": return "home-import-outline";
    case "growth": return "ruler";
    case "spawning": return "egg-outline";
    case "moved": return "swap-horizontal";
    case "died": return "candle";
    case "left": return "exit-to-app";
    case "anniversary": return "calendar-star";
  }
}

/**
 * The page of a named fish: its name, species and tank, the photos and videos newest first and the
 * timeline newest first. A fish that died shows as a memorial with its dates. The owner adds photos and
 * videos, growth, spawnings, a move to another tank and the death.
 */
export function FishScreen({ id }: { id: number }) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const { page, loading, error, reload, replace } = useFishPage(id);
  useEffect(() => {
    if (page) navigation.setOptions({ title: page.name });
  }, [page, navigation]);
  if (loading && !page) return <LoadingView />;
  if (error === "not_found") return <ErrorView message={t("fish.notFound")} />;
  if (error || !page) return <ErrorView message={t("fish.loadError")} onRetry={reload} />;
  return <FishPageView page={page} onChanged={replace} />;
}

function FishPageView({ page, onChanged }: { page: FishPage; onChanged: (page: FishPage) => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const router = useMachineRouter();
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const [error, setError] = useState<string | null>(null);
  const growth = latestGrowth(page);
  const saved = (next: FishPage) => { setSheet(null); setError(null); onChanged(next); };
  const text = { color: theme.colors.fg };
  const muted = { color: theme.colors.muted };
  async function change(action: () => Promise<FishPage>) {
    setError(null);
    try {
      onChanged(await action());
    } catch (err) {
      setError(translateApiError(err, t, "fish.saveError"));
    }
  }
  const speciesName = page.species?.displayName ?? t("fish.speciesUnknown");
  return <ScrollView testID="fish-page" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
    <View style={styles.page}>
      <View style={{ gap: theme.space[1] }}>
        <Text testID="fish-name" accessibilityRole="header" style={[theme.type.h1, text]}>{page.name}</Text>
        <Pressable testID="fish-species" accessibilityRole={page.species ? "button" : undefined} disabled={!page.species}
          onPress={() => page.species && router.push(`/species/${page.species.slug ?? page.species.id}`)} style={styles.line}>
          <Text style={[theme.type.body, { color: page.species ? theme.colors.accent : theme.colors.muted, fontStyle: "italic" }]}>{speciesName}</Text>
        </Pressable>
        <Pressable testID="fish-tank" accessibilityRole={page.tank.deleted ? undefined : "button"} disabled={page.tank.deleted}
          onPress={() => router.push(`/tanks/${page.tank.id}`)} style={styles.line}>
          <Text style={[theme.type.body, { color: page.tank.deleted ? theme.colors.muted : theme.colors.accent }]}>{tankText(page, t)}</Text>
        </Pressable>
        {page.isOwner ? null : <Pressable testID="fish-owner" accessibilityRole="button" onPress={() => router.push(`/profile/${page.owner.id}`)} style={styles.line}>
          <Text style={[theme.type.body, { color: theme.colors.accent }]}>{t("fish.keptBy", { name: page.owner.displayName ?? page.owner.username })}</Text>
        </Pressable>}
        {page.movedInOn ? <Text testID="fish-since" style={[theme.type.meta, muted]}>{t("fish.since", { date: formatLocalDate(page.movedInOn, i18n.language) })}</Text> : null}
        {growth ? <Text testID="fish-length" style={[theme.type.body, text]}>{t("fish.length", { length: lengthText(growth, system, i18n.language, page.isOwner) })}</Text> : null}
      </View>

      {page.memorial ? <View testID="fish-memorial" accessible accessibilityLabel={`${t("fish.memorial")}. ${memorialText(page, t, i18n.language)}`}
        style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
        <MaterialCommunityIcons name="candle" size={24} color={theme.colors.accent} />
        <View style={{ flex: 1 }}>
          <Text style={[theme.type.h2, text]}>{t("fish.memorial")}</Text>
          <Text testID="fish-memorial-dates" style={[theme.type.body, muted]}>{memorialText(page, t, i18n.language)}</Text>
        </View>
      </View> : null}

      {page.isOwner ? <View testID="fish-actions" style={{ gap: theme.space[2] }}>
        <Button testID="fish-add-photo" label={t("fish.actions.addPhoto")} onPress={() => setSheet("photo")} />
        {page.memorial ? null : <>
          <Button testID="fish-add-growth" variant="secondary" label={t("fish.actions.growth")} onPress={() => setSheet("growth")} />
          <Button testID="fish-add-spawning" variant="secondary" label={t("fish.actions.spawning")} onPress={() => setSheet("spawning")} />
          <Button testID="fish-move" variant="secondary" label={t("fish.actions.move")} onPress={() => setSheet("move")} />
          <Button testID="fish-death" variant="secondary" label={t("fish.actions.death")} onPress={() => setSheet("death")} />
        </>}
        {error ? <Text testID="fish-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      </View> : null}

      <Media page={page} onRemove={mediaItemId => void change(() => apiClient.fish.unlinkMedia(page.id, mediaItemId, localTime()))} />

      <View testID="fish-timeline" style={{ gap: theme.space[2] }}>
        <Text accessibilityRole="header" style={[theme.type.eyebrow, muted]}>{t("fish.timeline")}</Text>
        {page.timeline.length === 0 ? <Text testID="fish-timeline-empty" style={[theme.type.body, text]}>{t("fish.timelineEmpty")}</Text> : null}
        {page.timeline.map((item, index) => <TimelineRow key={`${item.kind}-${item.eventId ?? "derived"}-${item.on}-${index}`} item={item} owner={page.isOwner}
          onDelete={() => item.eventId !== null && void change(() => apiClient.fish.deleteEvent(page.id, item.eventId!, localTime()))} />)}
      </View>
    </View>

    {page.isOwner ? <>
      <AddMediaSheet page={page} visible={sheet === "photo"} onSaved={saved} onCancel={() => setSheet(null)} />
      <GrowthSheet page={page} visible={sheet === "growth"} onSaved={saved} onCancel={() => setSheet(null)} />
      <SpawningSheet page={page} visible={sheet === "spawning"} onSaved={saved} onCancel={() => setSheet(null)} />
      <MoveSheet page={page} visible={sheet === "move"} onSaved={saved} onCancel={() => setSheet(null)} />
      <DeathSheet page={page} visible={sheet === "death"} onSaved={saved} onCancel={() => setSheet(null)} />
    </> : null}
  </ScrollView>;
}

function TimelineRow({ item, owner, onDelete }: { item: FishTimelineItem; owner: boolean; onDelete: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const label = timelineLabel(item, owner, system, t, i18n.language);
  return <View testID={`fish-event-${item.kind}`} accessible accessibilityLabel={label}
    style={[styles.row, { borderColor: theme.colors.border }]}>
    <MaterialCommunityIcons name={iconOf(item.kind)} size={22} color={theme.colors.accent} />
    <View style={{ flex: 1 }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{timelineText(item, owner, system, t, i18n.language)}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{formatLocalDate(item.on, i18n.language)}</Text>
    </View>
    {owner && deletableItem(item) ? <Pressable testID={`fish-event-delete-${item.eventId}`} accessibilityRole="button"
      accessibilityLabel={t("fish.event.deleteFor", { date: formatLocalDate(item.on, i18n.language) })} onPress={onDelete} style={styles.action}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.danger }]}>{t("fish.event.delete")}</Text>
    </Pressable> : null}
  </View>;
}

function Media({ page, onRemove }: { page: FishPage; onRemove: (mediaItemId: number) => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const tile = Math.floor((Math.min(width, 560) - theme.space[4] * 2 - theme.space[2]) / 2);
  const empty = page.photos.length === 0 && page.videos.length === 0;
  const removeButton = (mediaItemId: number) => page.isOwner
    ? <Pressable testID={`fish-media-remove-${mediaItemId}`} accessibilityRole="button" accessibilityLabel={t("fish.removeMediaFor", { name: page.name })}
      onPress={() => onRemove(mediaItemId)} style={styles.action}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.danger }]}>{t("fish.removeMedia")}</Text>
    </Pressable> : null;
  return <View testID="fish-media" style={{ gap: theme.space[2] }}>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{t("fish.media")}</Text>
    {empty ? <Text testID="fish-media-empty" style={[theme.type.body, { color: theme.colors.fg }]}>{t("fish.mediaEmpty")}</Text> : null}
    {page.photos.length > 0 ? <View style={styles.grid}>
      {page.photos.map(photo => <View key={photo.mediaItemId} style={{ width: tile }}>
        <Image testID={`fish-photo-${photo.mediaItemId}`} source={photo.image.medium ?? photo.image.small ?? photo.image.thumb ?? undefined}
          accessibilityLabel={t("fish.photoOf", { name: page.name })} contentFit="cover"
          style={{ width: tile, height: tile, borderRadius: theme.radius.md, backgroundColor: theme.colors.placeholder }} />
        {removeButton(photo.mediaItemId)}
      </View>)}
    </View> : null}
    {page.videos.map(video => {
      const state = videoStateOf(video);
      return <View key={String(video.id)} testID={`fish-video-${video.id}`} style={{ gap: theme.space[1] }}>
        {state === "ready" && video.playlistUrl ? <VideoCard testID={`fish-video-${video.id}-card`} video={video} posterUrl={video.posterUrl ?? null} playlistUrl={video.playlistUrl} /> : null}
        {state === "processing" ? <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("video.processing")}</Text> : null}
        {state === "failed" ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.fg }]}>{t("video.failed")}</Text> : null}
        {removeButton(Number(video.id))}
      </View>;
    })}
  </View>;
}

const styles = StyleSheet.create({
  content: { padding: 16, paddingBottom: 32 },
  page: { width: "100%", maxWidth: 560, alignSelf: "center", gap: 16 },
  line: { minHeight: minTouchTarget, justifyContent: "center" },
  card: { flexDirection: "row", alignItems: "center", gap: 12, borderWidth: 1, padding: 16 },
  grid: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  row: { flexDirection: "row", alignItems: "center", gap: 12, minHeight: minTouchTarget, borderTopWidth: 1, paddingVertical: 8 },
  action: { minHeight: minTouchTarget, minWidth: minTouchTarget, justifyContent: "center", paddingHorizontal: 12 },
});
