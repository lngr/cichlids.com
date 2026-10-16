import { useCallback, useRef, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useFocusEffect } from "expo-router";
import { useTranslation } from "react-i18next";
import type { MyStory } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { EmptyView, ErrorView, LoadingView } from "../components/StatusView";
import { usePagedList } from "../hooks/usePagedList";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { minTouchTarget, useTheme } from "../theme";

const keyOf = (story: MyStory) => story.id;

/**
 * The stories of the signed-in member, drafts and published ones, the most recently changed first. A row opens
 * the editor; the button starts a new draft.
 */
export function StoriesView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const fetchPage = useCallback((offset: number, limit: number) => apiClient.storyEditor.list({ offset, limit }), []);
  const list = usePagedList(fetchPage, [], keyOf);
  const firstFocus = useRef(true);
  useFocusEffect(useCallback(() => {
    if (firstFocus.current) { firstFocus.current = false; return; }
    list.reload();
  }, [list.reload]));

  const create = async () => {
    setCreating(true);
    setCreateError(null);
    try {
      const story = await apiClient.storyEditor.create();
      router.push(`/stories/${story.id}`);
    } catch (error) {
      setCreateError(translateApiError(error, t, "stories.createError"));
    } finally {
      setCreating(false);
    }
  };

  const header = <View style={{ padding: theme.space[4], gap: theme.space[3] }}>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("stories.intro")}</Text>
    <Button testID="stories-new" label={t("stories.new")} busy={creating} onPress={() => void create()} />
    {createError ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{createError}</Text> : null}
  </View>;
  const empty = list.loading ? <LoadingView /> : list.error ? <ErrorView message={list.error} onRetry={list.reload} /> : <EmptyView message={t("stories.empty")} />;
  return <FlatList testID="stories-list" style={{ flex: 1, backgroundColor: theme.colors.bg }} data={list.items} keyExtractor={story => String(story.id)}
    ListHeaderComponent={header} ListEmptyComponent={empty} onEndReachedThreshold={0.4} onEndReached={list.error ? undefined : list.loadMore}
    refreshing={list.loading && list.items.length > 0} onRefresh={list.reload}
    renderItem={({ item }) => <Row story={item} onPress={() => router.push(`/stories/${item.id}`)} />} />;
}

function Row({ story, onPress }: { story: MyStory; onPress: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const title = story.title ?? t("stories.untitled");
  const state = t(`stories.state.${story.state}`);
  const picture = story.cover?.small ?? story.cover?.thumb ?? null;
  return <Pressable testID={`stories-row-${story.id}`} accessibilityRole="button" accessibilityLabel={`${title}, ${state}, ${t("stories.itemCount", { count: story.itemCount })}`}
    onPress={onPress} style={[styles.row, { borderColor: theme.colors.border, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md, marginHorizontal: theme.space[4], marginBottom: theme.space[2] }]}>
    <View style={[styles.thumb, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}>
      {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" /> : null}
    </View>
    <View style={{ flex: 1, gap: 2 }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]} numberOfLines={2}>{title}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{`${state} · ${t("stories.itemCount", { count: story.itemCount })}`}</Text>
    </View>
  </Pressable>;
}

const styles = StyleSheet.create({
  row: { flexDirection: "row", alignItems: "center", gap: 12, padding: 12, minHeight: minTouchTarget, borderWidth: 1 },
  thumb: { width: 56, height: 56, overflow: "hidden" },
});
