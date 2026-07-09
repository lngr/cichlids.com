import { useCallback, useEffect, useState } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { Chip } from "../../../src/components/Chip";
import { PictureCard } from "../../../src/components/PictureCard";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { PictureListItem } from "@cichlids/client-core";

const SORTS: { value: "newest" | "views" | "rating"; labelKey: string }[] = [
  { value: "newest", labelKey: "gallery.sort.newest" },
  { value: "views", labelKey: "gallery.sort.views" },
  { value: "rating", labelKey: "gallery.sort.rating" },
];

const TOPICS: { value: string | undefined; labelKey: string }[] = [
  { value: undefined, labelKey: "gallery.topics.all" },
  { value: "cichlids", labelKey: "gallery.topics.cichlids" },
  { value: "tanks", labelKey: "gallery.topics.tanks" },
  { value: "offtopic", labelKey: "gallery.topics.offtopic" },
  { value: "contest", labelKey: "gallery.topics.contest" },
];

export default function GalleryScreen() {
  const theme = useTheme();
  const router = useRouter();
  const { t } = useTranslation();
  const params = useLocalSearchParams<{ species?: string }>();
  const [sort, setSort] = useState<"newest" | "views" | "rating">("newest");
  const [topic, setTopic] = useState<string | undefined>(undefined);
  const [species, setSpecies] = useState<string | undefined>(undefined);

  // A species filter arrives as a route param when navigating here from a
  // species detail screen ("show this species' pictures"); it seeds local
  // state once so the chip filters above still work afterwards.
  useEffect(() => {
    if (params.species) setSpecies(params.species);
  }, [params.species]);

  const fetchPage = useCallback(
    (offset: number, limit: number) => apiClient.pictures.list({ sort, topic, species, offset, limit }),
    [sort, topic, species],
  );

  const { items, loading, loadingMore, error, loadMore, reload, total } = usePagedList<PictureListItem>(fetchPage, [sort, topic, species]);

  return (
    <View style={[styles.container, { backgroundColor: theme.colors.bg }]}>
      {species ? (
        <View style={styles.filterRow}>
          <Chip label={t("gallery.speciesFilter", { species })} active onPress={() => setSpecies(undefined)} />
        </View>
      ) : null}
      <View style={styles.filterRow}>
        {SORTS.map((option) => (
          <Chip key={option.value} label={t(option.labelKey)} active={sort === option.value} onPress={() => setSort(option.value)} />
        ))}
      </View>
      <View style={styles.filterRow}>
        {TOPICS.map((option) => (
          <Chip
            key={option.labelKey}
            label={t(option.labelKey)}
            active={topic === option.value}
            onPress={() => setTopic(option.value)}
          />
        ))}
      </View>

      {loading && items.length === 0 ? (
        <LoadingView label={t("gallery.loading")} />
      ) : error && items.length === 0 ? (
        <ErrorView message={error} onRetry={reload} />
      ) : items.length === 0 ? (
        <EmptyView message={t("gallery.empty")} />
      ) : (
        <FlatList
          key="grid-2"
          data={items}
          keyExtractor={(item) => item.slug}
          numColumns={2}
          contentContainerStyle={styles.listContent}
          renderItem={({ item }) => (
            <PictureCard picture={item} onPress={() => router.push(`/gallery/${item.slug}`)} />
          )}
          onEndReachedThreshold={0.4}
          onEndReached={loadMore}
          ListFooterComponent={loadingMore ? <LoadingView label={`${items.length} / ${total}`} /> : null}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  filterRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    paddingHorizontal: 12,
    paddingTop: 10,
  },
  listContent: {
    padding: 6,
    paddingBottom: 24,
  },
});
