import { useCallback, useEffect, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { CommentItem } from "../../../src/components/CommentItem";
import { DataRow } from "../../../src/components/DataRow";
import { MediaStrip } from "../../../src/components/MediaStrip";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { Comment, TankDetail } from "@cichlids/client-core";

export default function TankDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const tankId = Number(id);
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();

  const [tank, setTank] = useState<TankDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    apiClient.tanks
      .get(tankId)
      .then((detail) => {
        if (cancelled) return;
        setTank(detail);
        navigation.setOptions({ title: detail.title });
      })
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : "Fehler beim Laden"))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [tankId, navigation]);

  const fetchComments = useCallback((offset: number, limit: number) => apiClient.tanks.listComments(tankId, { offset, limit }), [tankId]);
  const comments = usePagedList<Comment>(fetchComments, [tankId]);

  if (loading) return <LoadingView label="Becken laden…" />;
  if (error || !tank) return <ErrorView message={error ?? "Becken nicht gefunden"} />;

  const dimensions = tank.dimensions
    ? `${tank.dimensions.width ?? "?"} × ${tank.dimensions.height ?? "?"} × ${tank.dimensions.depth ?? "?"} ${tank.dimensions.unit ?? ""}`
    : null;
  const water = tank.waterValues;
  const waterSummary = water
    ? [water.ph && `pH ${water.ph}`, water.kh && `KH ${water.kh}`, water.gh && `GH ${water.gh}`].filter(Boolean).join(" · ")
    : null;

  return (
    <FlatList
      style={{ backgroundColor: theme.colors.bg }}
      data={comments.items}
      keyExtractor={(item) => String(item.id)}
      renderItem={({ item }) => <CommentItem comment={item} />}
      onEndReachedThreshold={0.4}
      onEndReached={comments.loadMore}
      ListHeaderComponent={
        <View>
          <Image
            source={tank.mainImage?.large ?? tank.mainImage?.medium ?? undefined}
            style={[styles.heroImage, { backgroundColor: theme.colors.placeholder }]}
            contentFit="cover"
          />
          <View style={styles.body}>
            <Text style={[theme.type.h1, { color: theme.colors.fg }]}>{tank.title}</Text>
            <Pressable onPress={() => router.push(`/profile/${tank.author.id}`)}>
              <Text style={[theme.type.bodyStrong, { color: theme.colors.accent, marginTop: 4 }]}>
                {tank.author.displayName ?? tank.author.username}
              </Text>
            </Pressable>
            {tank.description ? (
              <Text style={[theme.type.body, { color: theme.colors.fg, marginTop: 10 }]}>{tank.description}</Text>
            ) : null}

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20, marginBottom: 4 }]}>Datenblatt</Text>
            <DataRow label="Maße" value={dimensions} />
            <DataRow label="Wasserwerte" value={waterSummary} />
            <DataRow label="Bodengrund" value={tank.gravel} />
            <DataRow label="Bepflanzung" value={tank.plants} />
            <DataRow label="Dekoration" value={tank.decoration} />
            <DataRow label="Licht" value={tank.light} />
            <DataRow label="Filterung" value={tank.filtration} />
            <DataRow label="Technik" value={tank.technic} />
            <DataRow label="Fütterung" value={tank.food} />
            <DataRow label="Notizen" value={tank.notes} />

            <MediaStrip title="Übersicht" items={tank.sections.showcase} />
            <MediaStrip title="Dekoration" items={tank.sections.decoration} />
            <MediaStrip title="Technik" items={tank.sections.technic} />

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20 }]}>Kommentare</Text>
          </View>
        </View>
      }
      ListEmptyComponent={comments.loading ? <LoadingView label="Kommentare laden…" /> : <EmptyView message="Noch keine Kommentare." />}
      ListFooterComponent={comments.loadingMore ? <LoadingView /> : null}
      contentContainerStyle={styles.listContent}
    />
  );
}

const styles = StyleSheet.create({
  heroImage: {
    width: "100%",
    aspectRatio: 4 / 3,
  },
  body: {
    padding: 16,
  },
  listContent: {
    paddingBottom: 32,
  },
});
