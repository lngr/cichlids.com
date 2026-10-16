import { useCallback, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import type { LibraryItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Chip } from "../components/Chip";
import { EmptyView, ErrorView, LoadingView } from "../components/StatusView";
import { usePagedList } from "../hooks/usePagedList";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { PhotoPickButton } from "../upload/PhotoPickButton";
import { MediaDetailSheet } from "./MediaDetailSheet";
import { listParams, tileImage, tileLabel, type KindFilter, type UseFilter } from "./libraryView";

const COLUMNS = 3;
const GAP = 6;

const KIND_FILTERS: KindFilter[] = ["all", "photo", "video"];
const USE_FILTERS: UseFilter[] = ["all", "linked", "unlinked"];

const itemKey = (item: LibraryItem) => item.id;

/**
 * The media library of the signed-in member: a grid of every photo and video the member owns, filtered by
 * kind and by whether a place shows the file. A tile opens the detail sheet with the places and the
 * deletion; the upload button puts a photo into the library without a place.
 */
export function MediaLibraryView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { width } = useWindowDimensions();
  const [kind, setKind] = useState<KindFilter>("all");
  const [use, setUse] = useState<UseFilter>("all");
  const [selected, setSelected] = useState<LibraryItem | null>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const fetchPage = useCallback(
    (offset: number, limit: number) => apiClient.library.list({ ...listParams(kind, use), offset, limit }),
    [kind, use],
  );
  const list = usePagedList(fetchPage, [kind, use], itemKey);

  const upload = async (file: Parameters<typeof apiClient.library.uploadPhoto>[0]) => {
    setUploadError(null);
    setUploading(true);
    try {
      await apiClient.library.uploadPhoto(file);
      list.reload();
    } catch (error) {
      setUploadError(translateApiError(error, t, "library.uploadError"));
    } finally {
      setUploading(false);
    }
  };

  const padding = theme.space[4];
  const tile = Math.floor((Math.min(width, 640) - padding * 2 - GAP * (COLUMNS - 1)) / COLUMNS);

  const header = (
    <View style={{ padding, gap: theme.space[3] }}>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("library.intro")}</Text>
      <PhotoPickButton testID="library-upload" label={uploading ? t("library.uploading") : t("library.upload")} variant="secondary" disabled={uploading}
        onPicked={(file) => void upload(file)} onError={() => setUploadError(t("library.pickError"))} />
      {uploadError ? <Text testID="library-upload-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{uploadError}</Text> : null}
      <View accessibilityRole="radiogroup" style={styles.chips}>
        {KIND_FILTERS.map((value) => <Chip key={value} testID={`library-kind-${value}`} label={t(`library.filter.kind.${value}`)} active={kind === value} onPress={() => setKind(value)} />)}
      </View>
      <View accessibilityRole="radiogroup" style={styles.chips}>
        {USE_FILTERS.map((value) => <Chip key={value} testID={`library-use-${value}`} label={t(`library.filter.use.${value}`)} active={use === value} onPress={() => setUse(value)} />)}
      </View>
    </View>
  );

  const empty = list.loading ? <LoadingView /> : list.error ? <ErrorView message={list.error} onRetry={list.reload} /> :
    <EmptyView message={t("library.empty")} />;
  const footer = list.loadingMore ? <LoadingView /> : list.items.length > 0 && list.error ? <ErrorView message={list.error} onRetry={list.reload} /> : null;

  return <View style={[styles.fill, { backgroundColor: theme.colors.bg }]}>
    <FlatList
      testID="library-grid"
      style={styles.list}
      contentContainerStyle={{ paddingBottom: theme.space[6] }}
      data={list.items}
      keyExtractor={(item) => String(item.id)}
      numColumns={COLUMNS}
      key={`library-grid-${tile}`}
      columnWrapperStyle={{ gap: GAP, paddingHorizontal: padding, marginBottom: GAP }}
      ListHeaderComponent={header}
      ListEmptyComponent={empty}
      ListFooterComponent={footer}
      onEndReachedThreshold={0.4}
      onEndReached={list.error ? undefined : list.loadMore}
      refreshing={list.loading && list.items.length > 0}
      onRefresh={list.reload}
      renderItem={({ item }) => <Tile item={item} size={tile} onPress={() => setSelected(item)} />}
    />
    <MediaDetailSheet item={selected} onClose={() => setSelected(null)} onDeleted={() => { setSelected(null); list.reload(); }} />
  </View>;
}

function Tile({ item, size, onPress }: { item: LibraryItem; size: number; onPress: () => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const picture = tileImage(item);
  const unused = item.placeCount === 0;
  return <Pressable testID={`library-tile-${item.id}`} onPress={onPress} accessibilityRole="button" accessibilityLabel={tileLabel(item, t, i18n.language)}
    style={[styles.tile, { width: size, height: size, borderRadius: theme.radius.md, backgroundColor: theme.colors.placeholder }]}>
    {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} /> : null}
    {item.kind === "video" ? <View style={[styles.play, { backgroundColor: theme.colors.surface }]}>
      <MaterialCommunityIcons name="play" size={18} color={theme.colors.fg} />
    </View> : null}
    <View testID={unused ? "library-tile-unused" : "library-tile-used"} style={[styles.badge, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border }]}>
      <Text style={[theme.type.meta, { color: unused ? theme.colors.muted : theme.colors.fg }]}>{unused ? t("library.badgeUnused") : String(item.placeCount)}</Text>
    </View>
  </Pressable>;
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  list: { flex: 1 },
  chips: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  tile: { overflow: "hidden" },
  badge: { position: "absolute", bottom: 4, start: 4, minWidth: 24, paddingHorizontal: 6, paddingVertical: 2, borderRadius: 999, borderWidth: 1, alignItems: "center" },
  play: { position: "absolute", top: 4, end: 4, width: 26, height: 26, borderRadius: 13, alignItems: "center", justifyContent: "center" },
});
