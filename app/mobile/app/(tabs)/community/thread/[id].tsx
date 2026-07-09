import { useEffect, useState } from "react";
import { FlatList, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams, useNavigation } from "expo-router";
import { apiClient } from "../../../../src/api/client";
import { useTheme } from "../../../../src/theme";
import { EmptyView, ErrorView, LoadingView } from "../../../../src/components/StatusView";
import { formatDate } from "../../../../src/lib/format";
import type { CommunityAttachment, CommunityPostDto, CommunityThreadDetail } from "@cichlids/client-core";

export default function ThreadDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const theme = useTheme();
  const navigation = useNavigation();

  const [thread, setThread] = useState<CommunityThreadDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    apiClient.community
      .getThread(Number(id))
      .then((detail) => {
        if (cancelled) return;
        setThread(detail);
        navigation.setOptions({ title: detail.title });
      })
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : "Fehler beim Laden"))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [id, navigation]);

  if (loading) return <LoadingView label="Thema laden…" />;
  if (error || !thread) return <ErrorView message={error ?? "Thema nicht gefunden"} />;

  return (
    <FlatList
      style={{ backgroundColor: theme.colors.bg }}
      data={thread.posts}
      keyExtractor={(item) => String(item.id)}
      renderItem={({ item }: { item: CommunityPostDto }) => (
        <View style={[styles.post, { borderColor: theme.colors.border }]}>
          <View style={styles.postHeader}>
            <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>
              {item.author.displayName ?? item.author.username ?? "Gast"}
            </Text>
            <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{formatDate(item.createdAt)}</Text>
          </View>
          <Text style={[theme.type.body, { color: theme.colors.fg, marginTop: 4 }]}>{item.body}</Text>
          {item.attachments.length > 0 ? (
            <View style={styles.attachments}>
              {item.attachments.map((attachment: CommunityAttachment) => (
                <Image
                  key={attachment.mediaItemId}
                  source={attachment.image.small ?? attachment.image.thumb ?? undefined}
                  style={[styles.attachmentImage, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}
                  contentFit="cover"
                />
              ))}
            </View>
          ) : null}
        </View>
      )}
      ListHeaderComponent={
        <View style={styles.header}>
          <Text style={[theme.type.h1, { color: theme.colors.fg }]}>{thread.title}</Text>
          <View style={[styles.notice, { backgroundColor: theme.colors.accentWeak }]}>
            <Text style={[theme.type.meta, { color: theme.colors.fg }]}>
              {thread.state === "archived"
                ? "Archivierter Thread aus dem alten Forum. Nur lesbar."
                : "Aus dem Archiv des alten Forums. Nur lesbar."}
            </Text>
          </View>
        </View>
      }
      ListEmptyComponent={<EmptyView message="Keine Beiträge in diesem Thema." />}
      contentContainerStyle={styles.listContent}
    />
  );
}

const styles = StyleSheet.create({
  header: { padding: 16, paddingBottom: 4 },
  notice: { marginTop: 10, padding: 10, borderRadius: 10 },
  post: {
    marginHorizontal: 16,
    paddingVertical: 12,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  postHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  attachments: {
    flexDirection: "row",
    gap: 8,
    marginTop: 8,
  },
  attachmentImage: {
    width: 80,
    height: 80,
  },
  listContent: { paddingBottom: 32 },
});
