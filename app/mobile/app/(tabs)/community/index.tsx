import { useCallback, useEffect, useState } from "react";
import { FlatList, StyleSheet, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { Chip } from "../../../src/components/Chip";
import { ThreadRow } from "../../../src/components/ThreadRow";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { CommunityCategory, CommunityThreadListItem } from "@cichlids/client-core";

const CATEGORY_LABELS: Record<string, string> = {
  cichlids: "Cichliden",
  african: "Afrika",
  market_place: "Marktplatz",
};

export default function CommunityScreen() {
  const theme = useTheme();
  const router = useRouter();
  const [category, setCategory] = useState<string | undefined>(undefined);
  const [categories, setCategories] = useState<CommunityCategory[]>([]);

  useEffect(() => {
    apiClient.community.listCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  const fetchPage = useCallback(
    (offset: number, limit: number) => apiClient.community.listThreads({ category, offset, limit }),
    [category],
  );
  const { items, loading, loadingMore, error, loadMore, reload, total } = usePagedList<CommunityThreadListItem>(fetchPage, [category]);

  return (
    <View style={[styles.container, { backgroundColor: theme.colors.bg }]}>
      <View style={[styles.notice, { backgroundColor: theme.colors.accentWeak }]}>
        <Text style={[theme.type.meta, { color: theme.colors.fg }]}>
          Archiv der alten Community-Foren. Nur lesbar, keine neuen Beiträge.
        </Text>
      </View>
      <View style={styles.filterRow}>
        <Chip label="Alle" active={category === undefined} onPress={() => setCategory(undefined)} />
        {categories.map((c) => (
          <Chip
            key={c.category}
            label={`${CATEGORY_LABELS[c.category] ?? c.category} (${Number(c.threadCount)})`}
            active={category === c.category}
            onPress={() => setCategory(c.category)}
          />
        ))}
      </View>

      {loading && items.length === 0 ? (
        <LoadingView label="Themen laden…" />
      ) : error && items.length === 0 ? (
        <ErrorView message={error} onRetry={reload} />
      ) : items.length === 0 ? (
        <EmptyView message="Keine Themen gefunden." />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => <ThreadRow thread={item} onPress={() => router.push(`/community/thread/${item.id}`)} />}
          onEndReachedThreshold={0.4}
          onEndReached={loadMore}
          ListFooterComponent={loadingMore ? <LoadingView label={`${items.length} / ${total}`} /> : null}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  notice: {
    margin: 12,
    padding: 10,
    borderRadius: 10,
  },
  filterRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    paddingHorizontal: 12,
    paddingBottom: 10,
  },
});
