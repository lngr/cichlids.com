import { useCallback, useState } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { Chip } from "../../../src/components/Chip";
import { TankCard } from "../../../src/components/TankCard";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { TankListItem } from "@cichlids/client-core";

const CATEGORIES: { value: string | undefined; label: string }[] = [
  { value: undefined, label: "Alle" },
  { value: "african", label: "Afrika" },
  { value: "american", label: "Amerika" },
  { value: "central_american", label: "Mittelamerika" },
  { value: "south_american", label: "Südamerika" },
  { value: "malawi", label: "Malawi" },
  { value: "tanganyika", label: "Tanganjika" },
  { value: "community", label: "Gesellschaft" },
];

export default function TanksScreen() {
  const theme = useTheme();
  const router = useRouter();
  const [category, setCategory] = useState<string | undefined>(undefined);

  const fetchPage = useCallback(
    (offset: number, limit: number) => apiClient.tanks.list({ category, offset, limit }),
    [category],
  );
  const { items, loading, loadingMore, error, loadMore, reload, total } = usePagedList<TankListItem>(fetchPage, [category]);

  return (
    <View style={[styles.container, { backgroundColor: theme.colors.bg }]}>
      <View style={styles.filterRow}>
        {CATEGORIES.map((option) => (
          <Chip key={option.label} label={option.label} active={category === option.value} onPress={() => setCategory(option.value)} />
        ))}
      </View>

      {loading && items.length === 0 ? (
        <LoadingView label="Becken laden…" />
      ) : error && items.length === 0 ? (
        <ErrorView message={error} onRetry={reload} />
      ) : items.length === 0 ? (
        <EmptyView message="Keine Becken gefunden." />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => <TankCard tank={item} onPress={() => router.push(`/tanks/${item.id}`)} />}
          onEndReachedThreshold={0.4}
          onEndReached={loadMore}
          contentContainerStyle={styles.listContent}
          ListFooterComponent={loadingMore ? <LoadingView label={`${items.length} / ${total}`} /> : null}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  filterRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    paddingHorizontal: 12,
    paddingTop: 10,
  },
  listContent: { paddingBottom: 24 },
});
