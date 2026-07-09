import { useCallback, useEffect, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { CommentItem } from "../../../src/components/CommentItem";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import { formatDate } from "../../../src/lib/format";
import type { Comment, PictureDetail } from "@cichlids/client-core";

export default function PictureDetailScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();

  const [picture, setPicture] = useState<PictureDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    apiClient.pictures
      .get(slug)
      .then((detail) => {
        if (cancelled) return;
        setPicture(detail);
        navigation.setOptions({ title: detail.title ?? "Bild" });
      })
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : "Fehler beim Laden"))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [slug, navigation]);

  const fetchComments = useCallback((offset: number, limit: number) => apiClient.pictures.listComments(slug, { offset, limit }), [slug]);
  const comments = usePagedList<Comment>(fetchComments, [slug]);

  if (loading) return <LoadingView label="Bild laden…" />;
  if (error || !picture) return <ErrorView message={error ?? "Bild nicht gefunden"} />;

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
            source={picture.image?.large ?? picture.image?.medium ?? undefined}
            style={[styles.heroImage, { backgroundColor: theme.colors.placeholder }]}
            contentFit="cover"
          />
          <View style={styles.body}>
            <Text style={[theme.type.h1, { color: theme.colors.fg }]}>{picture.title ?? "Ohne Titel"}</Text>
            <Pressable onPress={() => router.push(`/profile/${picture.author.id}`)}>
              <Text style={[theme.type.bodyStrong, { color: theme.colors.accent, marginTop: 4 }]}>
                {picture.author.displayName ?? picture.author.username}
              </Text>
            </Pressable>
            <Text style={[theme.type.meta, { color: theme.colors.muted, marginTop: 2 }]}>
              {formatDate(picture.publishedAt)} · {Number(picture.viewCount)} Aufrufe
              {picture.ratingAverage ? ` · ${"★".repeat(Math.round(Number(picture.ratingAverage)))} (${Number(picture.ratingCount)})` : ""}
            </Text>
            {picture.description ? (
              <Text style={[theme.type.body, { color: theme.colors.fg, marginTop: 10 }]}>{picture.description}</Text>
            ) : null}

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20 }]}>
              Kommentare ({Number(picture.commentCount)})
            </Text>
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
