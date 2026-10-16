import { useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { AppState, Modal, Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { createActor } from "xstate";
import { tankStoryMachine, type TankDetail, type TankTimeLapse } from "@cichlids/client-core";
import { useTranslation } from "react-i18next";
import { darkTheme, useTheme } from "../theme";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { TankMeasurements } from "../units/TankMeasurements";
import { DataRow } from "../components/DataRow";
import { MediaStrip } from "../components/MediaStrip";
import { Button } from "../components/Button";
import { formatDate } from "../lib/format";
import { TimeLapsePhoto, useReducedMotion } from "./TimeLapsePhoto";
import { TimeLapseScrubber } from "./TimeLapseScrubber";
import { useCareStream } from "./useCareStream";
import { CareStreamView } from "./CareStreamView";
import { formatCareAmount } from "./careAmount";
import { measurementLines } from "./measurementFormat";
import { useUnits } from "../units/useUnits";
import { useLocalSearchParams } from "expo-router";
import { TankRegionSlot } from "./TankRegionSlot";
import { TankVisibilitySettings } from "./TankVisibilitySettings";
import { VideoCard } from "../video/VideoCard";
import { TankVideosSection } from "../video/TankVideosSection";
import { useTankVideos } from "../video/useTankVideos";
import { videoStateOf } from "../video/videoState";

function photoUrl(image: TankTimeLapse["photos"][number]["image"]) {
  return image.large ?? image.medium ?? image.small ?? image.thumb ?? "";
}

export function TankExperience({ tank, series, children, onVisibilityChanged }: { tank: TankDetail; series: TankTimeLapse; children?: ReactNode; onVisibilityChanged?: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const stream = useCareStream(Number(tank.id), tank.author.id);
  const ownVideos = useTankVideos(Number(tank.id), stream.owner);
  const system = useUnits();
  const { view } = useLocalSearchParams<{ view?: string }>();
  const router = useMachineRouter();
  const reduced = useReducedMotion();
  const [actor] = useState(() => createActor(tankStoryMachine, { input: { count: series.photos.length } }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const playing = state.matches({ story: { player: "playing" } });
  const open = state.matches({ story: "player" });
  const care = state.matches("care");
  const index = state.context.index;
  const photo = series.photos[index];
  const date = (iso: string) => formatDate(iso, i18n.language);
  useEffect(() => {
    const upcoming = series.photos.slice(index + 1, index + 3).map(item => photoUrl(item.image));
    if (upcoming.length) void Image.prefetch(upcoming).catch(() => {});
  }, [index, series.photos]);
  const send = actor.send;
  useEffect(() => { if (view === "care") send({ type: "CARE" }); }, [view, send]);
  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => send({ type: "TICK" }), 1400);
    const appState = AppState.addEventListener("change", status => { if (status !== "active") send({ type: "PAUSE" }); });
    return () => { clearInterval(timer); appState.remove(); };
  }, [playing, send]);
  const text = { color: theme.colors.fg };
  const card = { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg };
  const postMedia = new Set(series.posts.flatMap(post => post.mediaItemIds.map(String)));
  const timeline = [
    ...stream.entries.map(entry => ({
      key: `care-${entry.id}`, date: entry.occurredAt,
      title: t(entry.kind === "measurement" ? "care.measure.entry" : "care.waterChange"),
      description: entry.kind === "measurement" ? measurementLines(entry, system, i18n.language).join("\n") : entry.amount ? formatCareAmount(entry.amount, i18n.language) : null,
      image: undefined, slug: null, video: null,
    })),
    ...series.photos.filter(item => !postMedia.has(String(item.mediaItemId))).map(item => ({
      key: `photo-${item.mediaItemId}`, date: item.takenAt ?? item.publishedAt,
      title: t("tankStory.photo"), description: null, image: item.image, slug: null, video: null,
    })),
    ...series.posts.map(post => ({
      key: `post-${post.id}`, date: post.publishedAt, title: post.title ?? t("tankStory.post"), description: post.description,
      image: series.photos.find(item => post.mediaItemIds.some(id => String(id) === String(item.mediaItemId)))?.image,
      slug: post.slug, video: null,
    })),
    ...(stream.owner ? ownVideos.videos.filter(video => videoStateOf(video) === "ready") : series.videos).map(video => ({
      key: `video-${video.id}`, date: video.createdAt, title: t("video.timeline"), description: null, image: undefined, slug: null, video,
    })),
  ].sort((a, b) => b.date.localeCompare(a.date) || b.key.localeCompare(a.key));
  function heading(label: string) { return <Text accessibilityRole="header" style={[theme.type.eyebrow, { color: theme.colors.muted, marginBottom: theme.space[3] }]}>{label}</Text>; }
  return <>
    <View style={styles.hero}>
      <Image source={tank.mainImage ? photoUrl(tank.mainImage) : series.photos.at(-1) ? photoUrl(series.photos.at(-1)!.image) : undefined}
        accessibilityLabel={t("tankStory.hero", { title: tank.title })} contentFit="cover" style={StyleSheet.absoluteFill} />
      <View style={[StyleSheet.absoluteFill, { backgroundColor: darkTheme.colors.bg, opacity: 0.4 }]} />
      <View style={styles.heroInfo}>
        <TankRegionSlot tank={tank} />
        <Text accessibilityRole="header" style={[theme.type.display, { color: darkTheme.colors.fg }]}>{tank.title}</Text>
        <Pressable accessibilityRole="button" accessibilityLabel={tank.author.displayName ?? tank.author.username}
          onPress={() => router.push(`/profile/${tank.author.id}`)} style={styles.owner}>
          {tank.author.avatarUrl ? <Image source={tank.author.avatarUrl} accessible={false} style={styles.avatar} /> : null}
          <Text style={[theme.type.meta, { color: darkTheme.colors.fg }]}>{tank.author.displayName ?? tank.author.username}</Text>
        </Pressable>
      </View>
    </View>
    {stream.owner ? <View style={styles.settings}>
      <TankVisibilitySettings tankId={Number(tank.id)} visibility={tank.visibility} onChanged={() => onVisibilityChanged?.()} />
    </View> : null}
    <View style={[styles.segment, { backgroundColor: theme.colors.accentWeak, borderRadius: theme.radius.md }]}>
      {(["story", "care"] as const).map(view => <Pressable key={view} testID={`tank-view-${view}`} accessibilityRole="tab"
        accessibilityState={{ selected: (view === "care") === care }} onPress={() => send({ type: view === "care" ? "CARE" : "STORY" })}
        style={[styles.segmentButton, { borderRadius: theme.radius.sm, backgroundColor: (view === "care") === care ? theme.colors.surface : undefined }]}>
        <Text style={[theme.type.bodyStrong, text]}>{t(`tankStory.${view}`)}</Text>
      </Pressable>)}
    </View>
    <View style={styles.body}>
      <View testID="tank-datasheet" style={styles.metrics}>
        <TankMeasurements tank={tank} layout="summary" />
        <View style={[styles.speciesMetric, card]}>
          <Text style={[theme.type.h1, text]}>{new Intl.NumberFormat(i18n.language).format(tank.inhabitants.length)}</Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("tankStory.species")}</Text>
        </View>
        <View style={[styles.speciesMetric, card]}>
          <Text style={[theme.type.h1, text]}>{new Intl.NumberFormat(i18n.language).format(series.photos.length)}</Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("tankStory.photos")}</Text>
        </View>
      </View>
      {care ? <>
        <View testID="tank-care-stream" style={[styles.card, card]}>
          <CareStreamView stream={stream} />
        </View>
        <View style={[styles.card, card]}>
          {heading(t("tanks.detail.waterValues"))}
          {Object.entries(tank.waterValues).filter(([, value]) => value != null && value !== "").length === 0
            ? <Text style={[theme.type.body, text]}>{t("tankStory.waterEmpty")}</Text> : null}
          {(["ph", "kh", "gh", "no2", "no3", "po4"] as const).map(key => <DataRow key={key} label={key === "ph" ? t("species.detail.ph") : key.toUpperCase()} value={tank.waterValues[key]} />)}
          <DataRow label={t("tanks.detail.notes")} value={tank.waterValues.notes} />
        </View>
        <View style={[styles.card, card]}>
          {heading(t("tanks.detail.technic"))}
          {[tank.gravel, tank.plants, tank.decoration, tank.light, tank.filtration, tank.technic].every(value => !value)
            ? <Text style={[theme.type.body, text]}>{t("tankStory.equipmentEmpty")}</Text> : null}
          <DataRow label={t("tanks.detail.substrate")} value={tank.gravel} />
          <DataRow label={t("tanks.detail.planting")} value={tank.plants} />
          <DataRow label={t("tanks.detail.decoration")} value={tank.decoration} />
          <DataRow label={t("tanks.detail.light")} value={[tank.light, tank.lightDuration].filter(Boolean).join(" / ")} />
          <DataRow label={t("tanks.detail.filtration")} value={tank.filtration} />
          <DataRow label={t("tanks.detail.technic")} value={tank.technic} />
          <DataRow label={t("tanks.detail.food")} value={tank.food} />
          <DataRow label={t("tanks.detail.notes")} value={tank.notes} />
          <MediaStrip title={t("tanks.detail.sectionDecoration")} items={tank.sections.decoration} />
          <MediaStrip title={t("tanks.detail.sectionTechnic")} items={tank.sections.technic} />
        </View>
      </> : <>
        <View style={[styles.card, card]}>
          {heading(t("tankStory.timeMachine"))}
          {series.isShowcase ? <Text testID="tank-showcase" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("tankStory.showcase")}</Text> : null}
          <Text style={[theme.type.meta, { color: theme.colors.muted, marginBottom: theme.space[3] }]}>
            {t("tankStory.photoCount", { total: new Intl.NumberFormat(i18n.language).format(series.photos.length) })}
          </Text>
          {series.photos.length >= 2 ? <Pressable testID="tank-time-machine-open" accessibilityRole="button"
            accessibilityLabel={t("tankStory.open")} onPress={() => send({ type: "OPEN" })} style={styles.preview}>
            <Image source={photoUrl(series.photos.at(-1)!.image)} accessibilityLabel={t("tankStory.hero", { title: tank.title })} contentFit="cover" style={StyleSheet.absoluteFill} />
            <View style={[styles.previewButton, { backgroundColor: theme.colors.accent, borderRadius: theme.radius.pill }]}>
              <Text style={[theme.type.bodyStrong, { color: theme.colors.onAccent }]}>{t("tankStory.play")}</Text>
            </View>
          </Pressable> : <Text style={[theme.type.body, text]}>{t("tankStory.photosEmpty")}</Text>}
          {stream.owner ? <View style={{ marginTop: theme.space[3] }}>
            <Button testID="tank-time-lapse-photo" variant="secondary" label={t("timeLapseCamera.action")} onPress={() => router.push(`/tanks/${tank.id}/camera`)} />
          </View> : null}
        </View>
        {stream.owner ? <View style={[styles.card, card]}><TankVideosSection tankId={Number(tank.id)} videos={ownVideos.videos} reload={ownVideos.reload} /></View> : null}
        {tank.description ? <Text style={[theme.type.body, text]}>{tank.description}</Text> : null}
        <View style={styles.card}>
          {heading(t("tankStory.timeline"))}
          {timeline.length === 0 ? <Text style={[theme.type.body, text]}>{t("tankStory.timelineEmpty")}</Text> : null}
          {timeline.map(event => <Pressable key={event.key} testID="tank-story-event" accessibilityRole={event.slug ? "button" : undefined}
            onPress={event.slug ? () => router.push(`/gallery/${event.slug}`) : undefined}
            style={[styles.event, { borderColor: theme.colors.border }]}>
            <View style={[styles.dot, { borderColor: theme.colors.border, backgroundColor: theme.colors.surface }]} />
            {event.image ? <Image source={event.image.thumb ?? photoUrl(event.image)} accessibilityLabel={event.title} style={[styles.thumb, { borderRadius: theme.radius.sm }]} /> : null}
            <View style={{ flex: 1 }}>
              <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{date(event.date)}</Text>
              <Text style={[theme.type.bodyStrong, text]}>{event.title}</Text>
              {event.description ? <Text style={[theme.type.body, text]}>{event.description}</Text> : null}
              {event.video?.playlistUrl ? <View style={{ marginTop: theme.space[2] }}>
                <VideoCard testID={`tank-timeline-video-${event.video.id}`} video={event.video} posterUrl={event.video.posterUrl ?? null} playlistUrl={event.video.playlistUrl} />
              </View> : null}
            </View>
          </Pressable>)}
          {stream.list.hasMore ? <Button testID="tank-story-care-more" variant="secondary" label={t("care.more")} busy={stream.list.loadingMore} onPress={stream.list.loadMore} /> : null}
        </View>
        <View style={[styles.card, card]}>
          {heading(t("tankStory.inhabitants"))}
          {tank.inhabitants.length === 0 ? <Text style={[theme.type.body, text]}>{t("tankStory.inhabitantsEmpty")}</Text> : null}
          {tank.inhabitants.map((item, i) => <Pressable key={i} accessibilityRole={item.species ? "button" : undefined}
            onPress={item.species ? () => router.push(`/species/${item.species!.slug ?? item.species!.id}`) : undefined}
            style={[styles.inhabitant, { borderColor: theme.colors.border }]}>
            <Text style={[theme.type.bodyStrong, text, { flex: 1, fontStyle: "italic" }]}>{item.species?.displayName ?? t("common.unknownValue")}</Text>
            <Text style={[theme.type.bodyStrong, text]}>{item.count == null ? t("common.unknownValue") : new Intl.NumberFormat(i18n.language).format(Number(item.count))}</Text>
          </Pressable>)}
        </View>
        {children}
      </>}
    </View>
    <Modal visible={open} animationType="none" onRequestClose={() => send({ type: "CLOSE" })}>
      <View accessibilityViewIsModal style={[styles.player, { backgroundColor: theme.colors.bg }]}>
        <ScrollView contentContainerStyle={{ flexGrow: 1, padding: theme.space[4], gap: theme.space[3] }}>
          <Button testID="tank-time-machine-close" label={t("tankStory.close")} variant="secondary" onPress={() => send({ type: "CLOSE" })} />
          <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("tankStory.timeMachine")} / {tank.title}</Text>
          {photo ? <TimeLapsePhoto uri={photoUrl(photo.image)} label={t("tankStory.frame", { title: tank.title, date: date(photo.takenAt ?? photo.publishedAt) })} reduced={reduced} /> : null}
          <Text testID="tank-time-machine-position" style={[theme.type.bodyStrong, text]}>{t("tankStory.position", { current: new Intl.NumberFormat(i18n.language).format(index + 1), total: new Intl.NumberFormat(i18n.language).format(series.photos.length) })}</Text>
          <Text style={[theme.type.bodyStrong, text]}>{photo ? date(photo.takenAt ?? photo.publishedAt) : ""}</Text>
          <TimeLapseScrubber index={index} count={series.photos.length} onSeek={value => send({ type: "SEEK", index: value })} />
          <View style={styles.controls}>
            <View style={{ flex: 1 }}><Button testID="tank-time-machine-previous" label={t("tankStory.previous")} variant="secondary" onPress={() => send({ type: "SEEK", index: index - 1 })} /></View>
            <View style={{ flex: 1 }}><Button testID={playing ? "tank-time-machine-pause" : "tank-time-machine-play"} label={t(playing ? "tankStory.pause" : "tankStory.play")} onPress={() => send({ type: playing ? "PAUSE" : "PLAY" })} /></View>
            <View style={{ flex: 1 }}><Button testID="tank-time-machine-next" label={t("tankStory.next")} variant="secondary" onPress={() => send({ type: "SEEK", index: index + 1 })} /></View>
          </View>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{series.photos.length ? t("tankStory.range", { start: date(series.photos[0].publishedAt), end: date(series.photos.at(-1)!.publishedAt) }) : ""}</Text>
        </ScrollView>
      </View>
    </Modal>
  </>;
}

const styles = StyleSheet.create({
  hero: { minHeight: 288, justifyContent: "flex-end" },
  heroInfo: { margin: 16, gap: 4 },
  owner: { minHeight: 48, flexDirection: "row", alignItems: "center", gap: 8 },
  avatar: { width: 24, height: 24, borderRadius: 12 },
  settings: { marginHorizontal: 16, marginTop: 16 },
  segment: { flexDirection: "row", marginHorizontal: 16, marginTop: 16, padding: 4 },
  segmentButton: { flex: 1, minHeight: 48, alignItems: "center", justifyContent: "center" },
  body: { padding: 16, gap: 16 },
  metrics: { flexDirection: "row", gap: 8 },
  speciesMetric: { flex: 0.7, minWidth: 0, borderWidth: 1, justifyContent: "center", alignItems: "center", padding: 8, gap: 4 },
  card: { padding: 16, borderWidth: 1, borderColor: "transparent" },
  preview: { height: 152, borderRadius: 12, overflow: "hidden", justifyContent: "center", alignItems: "center" },
  previewButton: { minHeight: 56, minWidth: 56, padding: 16, alignItems: "center", justifyContent: "center" },
  event: { flexDirection: "row", gap: 12, minHeight: 64, paddingBottom: 20, paddingStart: 20, borderStartWidth: 2 },
  dot: { position: "absolute", start: -7, top: 4, width: 12, height: 12, borderRadius: 6, borderWidth: 2 },
  thumb: { width: 48, height: 48 },
  inhabitant: { flexDirection: "row", gap: 12, minHeight: 48, alignItems: "center", borderTopWidth: 1 },
  player: { flex: 1, paddingTop: 32, paddingBottom: 16 },
  controls: { flexDirection: "row", gap: 8 },
});
