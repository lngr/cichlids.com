import { Image } from "expo-image";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { formatCount } from "../lib/format";
import type { StoryListItem } from "@cichlids/client-core";

export function StoryCard({ story, onPress }: { story: StoryListItem; onPress: () => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const viewCount = Number(story.viewCount);
  return (
    <Pressable
      style={styles.container}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={story.title ?? t("gallery.untitledAccessibility")}
      testID="picture-card"
    >
      <View style={[styles.imageWrap, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}>
        <Image
          source={story.cover?.small ?? story.cover?.thumb ?? undefined}
          style={styles.image}
          contentFit="cover"
          transition={150}
        />
        {Number(story.itemCount) > 1 ? (
          <View style={[styles.count, { backgroundColor: theme.colors.surface }]} testID="story-card-count">
            <Text style={[theme.type.meta, { color: theme.colors.fg }]}>{story.itemCount}</Text>
          </View>
        ) : null}
      </View>
      <Text numberOfLines={1} style={[theme.type.bodyStrong, { color: theme.colors.fg, marginTop: 6 }]}>
        {story.title ?? t("gallery.untitled")}
      </Text>
      <View style={styles.metaRow}>
        <Text numberOfLines={1} style={[theme.type.meta, { color: theme.colors.muted, flexShrink: 1 }]}>
          {story.author.displayName ?? story.author.username}
        </Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>
          {" · "}
          {formatCount(viewCount, i18n.language)} {t("gallery.views", { count: viewCount })}
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    margin: 6,
  },
  imageWrap: {
    aspectRatio: 1,
    overflow: "hidden",
  },
  count: {
    position: "absolute",
    top: 6,
    end: 6,
    minWidth: 24,
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 12,
    alignItems: "center",
  },
  image: {
    width: "100%",
    height: "100%",
  },
  metaRow: {
    flexDirection: "row",
    alignItems: "center",
  },
});
