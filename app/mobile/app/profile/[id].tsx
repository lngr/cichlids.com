import { useCallback, useEffect, useState } from "react";
import { FlatList, Image, StyleSheet, Text, View } from "react-native";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../../src/api/client";
import { useTheme } from "../../src/theme";
import { usePagedList } from "../../src/hooks/usePagedList";
import { Chip } from "../../src/components/Chip";
import { PictureCard } from "../../src/components/PictureCard";
import { TankCard } from "../../src/components/TankCard";
import { EmptyView, ErrorView, LoadingView } from "../../src/components/StatusView";
import { formatDate } from "../../src/lib/format";
import type { PictureListItem, ProfileDetail, TankListItem } from "@cichlids/client-core";

export default function ProfileScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const profileId = Number(id);
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();
  const { t, i18n } = useTranslation();

  const [profile, setProfile] = useState<ProfileDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<"pictures" | "tanks">("pictures");

  useEffect(() => {
    let cancelled = false;
    apiClient.profiles
      .get(profileId)
      .then((detail) => {
        if (cancelled) return;
        setProfile(detail);
        navigation.setOptions({ title: detail.displayName ?? detail.username });
      })
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : t("common.loadError")))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [profileId, navigation, t]);

  const fetchPictures = useCallback((offset: number, limit: number) => apiClient.profiles.listPictures(profileId, { offset, limit }), [profileId]);
  const fetchTanks = useCallback((offset: number, limit: number) => apiClient.profiles.listTanks(profileId, { offset, limit }), [profileId]);
  const pictures = usePagedList<PictureListItem>(fetchPictures, [profileId]);
  const tanks = usePagedList<TankListItem>(fetchTanks, [profileId]);

  if (loading) return <LoadingView label={t("profile.loading")} />;
  if (error || !profile) return <ErrorView message={error ?? t("profile.notFound")} />;

  const header = (
    <View>
      <View style={styles.headerRow}>
        {profile.avatarUrl ? (
          <Image source={{ uri: profile.avatarUrl }} style={[styles.avatar, { backgroundColor: theme.colors.placeholder }]} />
        ) : (
          <View style={[styles.avatar, { backgroundColor: theme.colors.placeholder }]} />
        )}
        <View style={{ flex: 1 }}>
          <Text style={[theme.type.h1, { color: theme.colors.fg }]}>{profile.displayName ?? profile.username}</Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>
            {[profile.city, profile.countryCode].filter(Boolean).join(", ") ||
              t("profile.memberSince", { date: formatDate(profile.createdAt, i18n.language) })}
          </Text>
        </View>
      </View>
      <View style={styles.statsRow}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.pictures", { count: Number(profile.stats.pictureCount) })}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.tanks", { count: Number(profile.stats.tankCount) })}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.comments", { count: Number(profile.stats.commentCount) })}</Text>
      </View>
      <View style={styles.filterRow}>
        <Chip label={t("profile.picturesTab")} active={tab === "pictures"} onPress={() => setTab("pictures")} />
        <Chip label={t("profile.tanksTab")} active={tab === "tanks"} onPress={() => setTab("tanks")} />
      </View>
    </View>
  );

  if (tab === "pictures") {
    return (
      <FlatList
        key="pictures-grid"
        style={{ backgroundColor: theme.colors.bg }}
        data={pictures.items}
        keyExtractor={(item) => item.slug}
        numColumns={2}
        ListHeaderComponent={header}
        contentContainerStyle={styles.content}
        renderItem={({ item }) => <PictureCard picture={item} onPress={() => router.push(`/gallery/${item.slug}`)} />}
        onEndReachedThreshold={0.4}
        onEndReached={pictures.loadMore}
        ListEmptyComponent={pictures.loading ? <LoadingView /> : <EmptyView message={t("profile.emptyPictures")} />}
        ListFooterComponent={pictures.loadingMore ? <LoadingView /> : null}
      />
    );
  }

  return (
    <FlatList
      key="tanks-list"
      style={{ backgroundColor: theme.colors.bg }}
      data={tanks.items}
      keyExtractor={(item) => String(item.id)}
      ListHeaderComponent={header}
      contentContainerStyle={styles.content}
      renderItem={({ item }) => <TankCard tank={item} onPress={() => router.push(`/tanks/${item.id}`)} />}
      onEndReachedThreshold={0.4}
      onEndReached={tanks.loadMore}
      ListEmptyComponent={tanks.loading ? <LoadingView /> : <EmptyView message={t("profile.emptyTanks")} />}
      ListFooterComponent={tanks.loadingMore ? <LoadingView /> : null}
    />
  );
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    padding: 16,
  },
  avatar: {
    width: 64,
    height: 64,
    borderRadius: 32,
  },
  statsRow: {
    flexDirection: "row",
    gap: 16,
    paddingHorizontal: 16,
  },
  filterRow: {
    flexDirection: "row",
    gap: 8,
    padding: 16,
  },
  content: { paddingBottom: 24 },
});
