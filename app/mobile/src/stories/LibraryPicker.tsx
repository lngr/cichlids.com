import { useCallback } from "react";
import { FlatList, Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { LibraryItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { EmptyView, ErrorView, LoadingView } from "../components/StatusView";
import { usePagedList } from "../hooks/usePagedList";
import { tileImage } from "../library/libraryView";
import { formatDate } from "../lib/format";
import { useTheme } from "../theme";

const COLUMNS = 3;

/**
 * A sheet with the files of the media library to choose from. A tile adds the file to the story; files the story
 * shows already are marked and cannot be chosen twice.
 */
export function LibraryPicker({ visible, taken, onPick, onClose }: {
  visible: boolean; taken: ReadonlySet<number>; onPick: (item: LibraryItem) => void; onClose: () => void;
}) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const fetchPage = useCallback((offset: number, limit: number) => apiClient.library.list({ offset, limit }), []);
  const list = usePagedList(fetchPage, [visible], (item: LibraryItem) => item.id);
  const empty = list.loading ? <LoadingView /> : list.error ? <ErrorView message={list.error} onRetry={list.reload} /> : <EmptyView message={t("library.empty")} />;
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.45)" }}>
      <View testID="library-picker" style={{ height: "88%", width: "100%", maxWidth: 640, alignSelf: "center", backgroundColor: theme.colors.bg,
        borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet }}>
        <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg, padding: theme.space[4] }]}>{t("stories.picker.libraryTitle")}</Text>
        <FlatList style={{ flex: 1 }} data={list.items} numColumns={COLUMNS} keyExtractor={item => String(item.id)} key="library-picker-grid"
          columnWrapperStyle={{ gap: 6, paddingHorizontal: theme.space[4], marginBottom: 6 }} ListEmptyComponent={empty}
          onEndReachedThreshold={0.4} onEndReached={list.error ? undefined : list.loadMore}
          renderItem={({ item }) => {
            const used = taken.has(item.id);
            const label = `${t(`library.kind.${item.kind}`)}, ${formatDate(item.createdAt, i18n.language)}${used ? `, ${t("stories.picker.taken")}` : ""}`;
            const picture = tileImage(item);
            return <Pressable testID={`library-pick-${item.id}`} accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled: used }}
              disabled={used} onPress={() => onPick(item)}
              style={[styles.tile, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md, opacity: used ? 0.4 : 1 }]}>
              {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" /> : null}
            </Pressable>;
          }} />
        <View style={{ padding: theme.space[4] }}>
          <Button testID="library-picker-close" variant="secondary" label={t("stories.picker.close")} onPress={onClose} />
        </View>
      </View>
    </View>
  </Modal>;
}

const styles = StyleSheet.create({ tile: { flex: 1 / COLUMNS, aspectRatio: 1, overflow: "hidden" } });
