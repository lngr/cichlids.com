import { Image } from "expo-image";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { formatCount } from "../lib/format";
import type { PictureListItem } from "@cichlids/client-core";

export function PictureCard({ picture, onPress }: { picture: PictureListItem; onPress: () => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const viewCount = Number(picture.viewCount);
  return (
    <Pressable
      style={styles.container}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={picture.title ?? t("gallery.untitledAccessibility")}
      testID="picture-card"
    >
      <View style={[styles.imageWrap, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}>
        <Image
          source={picture.image?.small ?? picture.image?.thumb ?? undefined}
          style={styles.image}
          contentFit="cover"
          transition={150}
        />
      </View>
      <Text numberOfLines={1} style={[theme.type.bodyStrong, { color: theme.colors.fg, marginTop: 6 }]}>
        {picture.title ?? t("gallery.untitled")}
      </Text>
      <View style={styles.metaRow}>
        <Text numberOfLines={1} style={[theme.type.meta, { color: theme.colors.muted, flexShrink: 1 }]}>
          {picture.author.displayName ?? picture.author.username}
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
  image: {
    width: "100%",
    height: "100%",
  },
  metaRow: {
    flexDirection: "row",
    alignItems: "center",
  },
});
