import { useMachineRouter } from "../navigation/NavigationProvider";
import type { ReactElement } from "react";
import { FlatList } from "react-native";

import { useTranslation } from "react-i18next";
import type { StoryListItem, TankListItem } from "@cichlids/client-core";
import type { UsePagedListState } from "../hooks/usePagedList";
import { StoryCard } from "../components/StoryCard";
import { TankCard } from "../components/TankCard";
import { EmptyView, ErrorView, LoadingView } from "../components/StatusView";
import { useTheme } from "../theme";

export function ProfileContentList({ section, pictures, tanks, header }: {
  section: "pictures" | "tanks";
  pictures: UsePagedListState<StoryListItem>;
  tanks: UsePagedListState<TankListItem>;
  header: ReactElement | null;
}) {
  const theme = useTheme();
  const router = useMachineRouter();
  const { t } = useTranslation();
  const list = section === "pictures" ? pictures : tanks;
  const empty = list.loading ? <LoadingView /> : list.error ? <ErrorView message={list.error} onRetry={list.reload} /> :
    <EmptyView message={t(section === "pictures" ? "profile.emptyPictures" : "profile.emptyTanks")} />;
  const footer = list.loadingMore ? <LoadingView /> : list.items.length > 0 && list.error ?
    <ErrorView message={list.error} onRetry={list.reload} /> : null;
  const common = {
    style: { backgroundColor: theme.colors.bg },
    contentContainerStyle: { paddingBottom: theme.space[6] },
    ListHeaderComponent: header,
    ListEmptyComponent: empty,
    ListFooterComponent: footer,
    onEndReachedThreshold: 0.4,
    onEndReached: list.error ? undefined : list.loadMore,
    refreshing: list.loading && list.items.length > 0,
    onRefresh: list.reload,
  };
  if (section === "pictures") {
    return <FlatList {...common} key="pictures-grid" data={pictures.items} keyExtractor={(item) => item.slug} numColumns={2}
      renderItem={({ item }) => <StoryCard story={item} onPress={() => router.push(`/gallery/${item.slug}`)} />} />;
  }
  return <FlatList {...common} key="tanks-list" data={tanks.items} keyExtractor={(item) => String(item.id)}
    renderItem={({ item }) => <TankCard tank={item} onPress={() => router.push(`/tanks/${item.id}`)} />} />;
}
