import { Image } from "expo-image";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import type { TankListItem } from "@cichlids/client-core";

export function TankCard({ tank, onPress }: { tank: TankListItem; onPress: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const imageCount = Number(tank.imageCount);
  return (
    <Pressable
      style={[styles.container, { backgroundColor: theme.colors.surface, borderRadius: theme.radius.lg, borderColor: theme.colors.border }]}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={tank.title}
      testID="tank-card"
    >
      <Image
        source={tank.mainImage?.small ?? tank.mainImage?.thumb ?? undefined}
        style={[styles.image, { backgroundColor: theme.colors.placeholder, borderTopLeftRadius: theme.radius.lg, borderTopRightRadius: theme.radius.lg }]}
        contentFit="cover"
      />
      <View style={styles.body}>
        <Text numberOfLines={1} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{tank.title}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted, marginTop: 2 }]}>
          {tank.author.displayName ?? tank.author.username}
          {tank.category ? ` · ${t(`tanks.categories.${tank.category}`)}` : ""} · {t("tanks.pictures", { count: imageCount })}
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: {
    marginHorizontal: 12,
    marginVertical: 6,
    overflow: "hidden",
    borderWidth: 1,
  },
  image: {
    width: "100%",
    aspectRatio: 16 / 9,
  },
  body: {
    padding: 12,
  },
});
