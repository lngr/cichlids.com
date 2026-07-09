import { Image } from "expo-image";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { useTheme } from "../theme";
import type { TankMediaItem } from "@cichlids/client-core";

export function MediaStrip({ title, items }: { title: string; items: TankMediaItem[] }) {
  const theme = useTheme();
  if (items.length === 0) return null;
  return (
    <View style={styles.container}>
      <Text style={[theme.type.h2, { color: theme.colors.fg, marginBottom: 8 }]}>{title}</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.strip}>
        {items.map((item) => (
          <Image
            key={item.mediaItemId}
            source={item.image.small ?? item.image.thumb ?? undefined}
            style={[styles.image, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}
            contentFit="cover"
          />
        ))}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    marginTop: 16,
  },
  strip: {
    gap: 8,
  },
  image: {
    width: 120,
    height: 120,
  },
});
