import { useState } from "react";
import { FlatList, StyleSheet, Text, View, type NativeScrollEvent, type NativeSyntheticEvent } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { StoryItem } from "@cichlids/client-core";
import { useTheme } from "../theme";
import { VideoCard } from "../video/VideoCard";
import { SubjectCard } from "./SubjectCard";
import type { SequencePage } from "./storySequence";

const PAGE_ASPECT = 4 / 3;

function itemImage(item: StoryItem) {
  return item.image?.large ?? item.image?.medium ?? item.image?.small ?? item.image?.thumb ?? undefined;
}

/**
 * The swipeable sequence of a story: one page per item with its caption and one page per positioned
 * subject card, in the order they share.
 */
export function StorySequence({ pages, onOpenSubject }: { pages: SequencePage[]; onOpenSubject: (route: string) => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const [width, setWidth] = useState(0);
  const [index, setIndex] = useState(0);
  const onScroll = (event: NativeSyntheticEvent<NativeScrollEvent>) => {
    if (width > 0) setIndex(Math.min(pages.length - 1, Math.max(0, Math.round(event.nativeEvent.contentOffset.x / width))));
  };
  return (
    <View testID="story-sequence" onLayout={event => setWidth(event.nativeEvent.layout.width)}>
      {width > 0 ? (
        <FlatList
          horizontal
          pagingEnabled
          data={pages}
          keyExtractor={page => `${page.kind}-${page.kind === "item" ? page.item.id : page.subject.id}`}
          showsHorizontalScrollIndicator={false}
          onScroll={onScroll}
          scrollEventThrottle={16}
          getItemLayout={(_, position) => ({ length: width, offset: width * position, index: position })}
          renderItem={({ item: page, index: position }) => (
            <View
              testID={`story-page-${position}`}
              accessibilityLabel={t(page.kind === "item" ? "gallery.story.photo" : "gallery.story.position", { current: position + 1, total: pages.length })}
              style={{ width }}
            >
              {page.kind === "item" ? (
                <>
                  {page.item.video && page.item.video.playlistUrl ? (
                    <VideoCard video={page.item.video} posterUrl={page.item.video.posterUrl ?? null} playlistUrl={page.item.video.playlistUrl} testID={`story-video-${page.item.id}`} />
                  ) : (
                    <Image
                      testID={`story-item-${page.item.id}`}
                      source={itemImage(page.item)}
                      style={[styles.image, { backgroundColor: theme.colors.placeholder }]}
                      contentFit="cover"
                    />
                  )}
                  {page.item.caption ? (
                    <Text testID="story-caption" style={[theme.type.body, styles.caption, { color: theme.colors.fg }]}>{page.item.caption}</Text>
                  ) : null}
                </>
              ) : (
                <View style={[styles.cardPage, { aspectRatio: PAGE_ASPECT }]}>
                  <SubjectCard subject={page.subject} onOpen={onOpenSubject} fill />
                </View>
              )}
            </View>
          )}
        />
      ) : null}
      {pages.length > 1 ? (
        <Text testID="story-position" style={[theme.type.meta, styles.position, { color: theme.colors.muted }]}>
          {t("gallery.story.position", { current: index + 1, total: pages.length })}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  image: {
    width: "100%",
    aspectRatio: PAGE_ASPECT,
  },
  caption: {
    paddingHorizontal: 16,
    paddingTop: 10,
  },
  cardPage: {
    width: "100%",
    padding: 16,
  },
  position: {
    textAlign: "center",
    marginTop: 8,
  },
});
