import { useCallback, useEffect, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
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
  const { t } = useTranslation();

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
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : t("common.loadError")))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [tankId, navigation, t]);

  const fetchComments = useCallback((offset: number, limit: number) => apiClient.tanks.listComments(tankId, { offset, limit }), [tankId]);
  const comments = usePagedList<Comment>(fetchComments, [tankId]);

  if (loading) return <LoadingView label={t("tanks.detail.loading")} />;
  if (error || !tank) return <ErrorView message={error ?? t("tanks.detail.notFound")} />;

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

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20, marginBottom: 4 }]}>{t("tanks.detail.datasheet")}</Text>
            <DataRow label={t("tanks.detail.dimensions")} value={dimensions} />
            <DataRow label={t("tanks.detail.waterValues")} value={waterSummary} />
            <DataRow label={t("tanks.detail.substrate")} value={tank.gravel} />
            <DataRow label={t("tanks.detail.planting")} value={tank.plants} />
            <DataRow label={t("tanks.detail.decoration")} value={tank.decoration} />
            <DataRow label={t("tanks.detail.light")} value={tank.light} />
            <DataRow label={t("tanks.detail.filtration")} value={tank.filtration} />
            <DataRow label={t("tanks.detail.technic")} value={tank.technic} />
            <DataRow label={t("tanks.detail.food")} value={tank.food} />
            <DataRow label={t("tanks.detail.notes")} value={tank.notes} />

            <MediaStrip title={t("tanks.detail.sectionOverview")} items={tank.sections.showcase} />
            <MediaStrip title={t("tanks.detail.sectionDecoration")} items={tank.sections.decoration} />
            <MediaStrip title={t("tanks.detail.sectionTechnic")} items={tank.sections.technic} />

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20 }]}>{t("tanks.detail.comments")}</Text>
          </View>
        </View>
      }
      ListEmptyComponent={comments.loading ? <LoadingView label={t("tanks.detail.commentsLoading")} /> : <EmptyView message={t("tanks.detail.commentsEmpty")} />}
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
