import { useCallback, useEffect, useState } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { SearchField } from "../../../src/components/SearchField";
import { SpeciesRow } from "../../../src/components/SpeciesRow";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { SpeciesListItem } from "@cichlids/client-core";

export default function SpeciesScreen() {
  const theme = useTheme();
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");

  useEffect(() => {
    const timeout = setTimeout(() => setDebouncedQuery(query.trim()), 300);
    return () => clearTimeout(timeout);
  }, [query]);

  const fetchPage = useCallback(
    (offset: number, limit: number) => apiClient.species.list({ query: debouncedQuery || undefined, offset, limit }),
    [debouncedQuery],
  );
  const { items, loading, loadingMore, error, loadMore, reload, total } = usePagedList<SpeciesListItem>(fetchPage, [debouncedQuery]);

  return (
    <View style={[styles.container, { backgroundColor: theme.colors.bg }]}>
      <SearchField value={query} onChangeText={setQuery} placeholder="Gattung oder Art suchen…" />

      {loading && items.length === 0 ? (
        <LoadingView label="Arten laden…" />
      ) : error && items.length === 0 ? (
        <ErrorView message={error} onRetry={reload} />
      ) : items.length === 0 ? (
        <EmptyView message="Keine Arten gefunden." />
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => String(item.id)}
          renderItem={({ item }) => (
            <SpeciesRow species={item} onPress={() => router.push(`/species/${item.slug ?? item.id}`)} />
          )}
          onEndReachedThreshold={0.4}
          onEndReached={loadMore}
          style={styles.list}
          ListFooterComponent={loadingMore ? <LoadingView label={`${items.length} / ${total}`} /> : null}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1 },
  list: { marginTop: 4 },
});
