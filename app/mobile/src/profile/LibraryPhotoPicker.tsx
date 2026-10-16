import { useCallback } from "react";
import { FlatList, Modal, Pressable, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { LibraryItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { EmptyView, ErrorView, LoadingView } from "../components/StatusView";
import { usePagedList } from "../hooks/usePagedList";
import { formatDate } from "../lib/format";
import { tileImage } from "../library/libraryView";
import { useTheme } from "../theme";

const COLUMNS = 3;
const GAP = 6;
const itemKey = (item: LibraryItem) => item.id;

/**
 * A sheet with the photos of the media library of the member. A tap on a photo hands it to onPicked;
 * the sheet closes through onClose.
 */
export function LibraryPhotoPicker({ visible, onClose, onPicked }: {
  visible: boolean;
  onClose: () => void;
  onPicked: (item: LibraryItem) => void;
}) {
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
    <View style={styles.backdrop}>
      {visible ? <PickerContent onClose={onClose} onPicked={onPicked} /> : null}
    </View>
  </Modal>;
}

function PickerContent({ onClose, onPicked }: { onClose: () => void; onPicked: (item: LibraryItem) => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const { width } = useWindowDimensions();
  const fetchPage = useCallback((offset: number, limit: number) => apiClient.library.list({ kind: "photo", offset, limit }), []);
  const list = usePagedList(fetchPage, [], itemKey);
  const padding = theme.space[4];
  const tile = Math.floor((Math.min(width, 640) - padding * 2 - GAP * (COLUMNS - 1)) / COLUMNS);

  const empty = list.loading ? <LoadingView /> : list.error ? <ErrorView message={list.error} onRetry={list.reload} /> :
    <EmptyView message={t("profileImages.picker.empty")} />;
  const footer = list.loadingMore ? <LoadingView /> : null;

  return <View testID="profile-images-picker" style={[styles.sheet, { backgroundColor: theme.colors.bg, borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet }]}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg, padding }]}>{t("profileImages.picker.title")}</Text>
    <FlatList
      style={styles.list}
      data={list.items}
      keyExtractor={(item) => String(item.id)}
      numColumns={COLUMNS}
      key={`picker-grid-${tile}`}
      columnWrapperStyle={{ gap: GAP, paddingHorizontal: padding, marginBottom: GAP }}
      ListEmptyComponent={empty}
      ListFooterComponent={footer}
      onEndReachedThreshold={0.4}
      onEndReached={list.error ? undefined : list.loadMore}
      renderItem={({ item }) => {
        const picture = tileImage(item);
        return <Pressable testID={`profile-images-pick-${item.id}`} accessibilityRole="button"
          accessibilityLabel={t("profileImages.picker.photo", { date: formatDate(item.createdAt, i18n.language) })}
          onPress={() => onPicked(item)}
          style={[styles.tile, { width: tile, height: tile, borderRadius: theme.radius.md, backgroundColor: theme.colors.placeholder }]}>
          {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" transition={150} /> : null}
        </Pressable>;
      }}
    />
    <View style={{ padding }}>
      <Button testID="profile-images-picker-cancel" variant="secondary" label={t("profileImages.picker.cancel")} onPress={onClose} />
    </View>
  </View>;
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.45)" },
  sheet: { maxHeight: "92%", minHeight: 320, width: "100%", maxWidth: 640, alignSelf: "center" },
  list: { flexGrow: 0 },
  tile: { overflow: "hidden" },
});
