import { useState } from "react";
import { Image } from "expo-image";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { ShareActions } from "../stories/ShareActions";
import { useTheme } from "../theme";
import type { TankMediaItem } from "@cichlids/client-core";

/** A strip of photos; for the owner a photo opens its gallery actions. */
export function MediaStrip({ title, items, own = false }: { title: string; items: TankMediaItem[]; own?: boolean }) {
  const theme = useTheme();
  const [selected, setSelected] = useState<number | null>(null);
  if (items.length === 0) return null;
  return (
    <View style={styles.container}>
      <Text style={[theme.type.h2, { color: theme.colors.fg, marginBottom: 8 }]}>{title}</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.strip}>
        {items.map((item) => {
          const picture = <Image
            source={item.image.small ?? item.image.thumb ?? undefined}
            style={[styles.image, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}
            accessibilityLabel={title}
            contentFit="cover"
          />;
          return own
            ? <Pressable key={item.mediaItemId} testID={`media-strip-${item.mediaItemId}`} accessibilityRole="button" accessibilityLabel={title}
              onPress={() => setSelected(selected === Number(item.mediaItemId) ? null : Number(item.mediaItemId))}>{picture}</Pressable>
            : <View key={item.mediaItemId}>{picture}</View>;
        })}
      </ScrollView>
      {own && selected !== null ? <View style={{ marginTop: 8 }}><ShareActions key={selected} mediaItemId={selected} /></View> : null}
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
