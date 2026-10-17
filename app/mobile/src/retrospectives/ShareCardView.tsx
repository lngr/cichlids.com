import { forwardRef } from "react";
import { StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import type { ShareCardItem } from "./shareCardLayout";
import { SHARE_CARD_COLORS, SHARE_CARD_UNITS, SHARE_CARD_UNITS_HEIGHT } from "./shareCardModel";

/**
 * The shareable card as a portrait view of the given width. Its colours are fixed, so the image
 * looks the same in the light and the dark theme. The photo reports whether it loaded.
 */
export const ShareCardView = forwardRef<View, {
  items: ShareCardItem[];
  photoUrl: string | null;
  width: number;
  onPhotoState: (state: "loaded" | "failed") => void;
}>(function ShareCardView({ items, photoUrl, width, onPhotoState }, ref) {
  const scale = width / SHARE_CARD_UNITS;
  return <View ref={ref} collapsable={false} testID="share-card" accessibilityLabel={items.filter(item => item.kind === "text").map(item => item.text).join(", ")}
    style={[styles.card, { width, height: SHARE_CARD_UNITS_HEIGHT * scale, backgroundColor: SHARE_CARD_COLORS.background }]}>
    {items.map((item, index) => {
      const box = { position: "absolute" as const, start: item.x * scale, top: item.y * scale, width: item.width * scale, height: item.height * scale };
      if (item.kind === "photo") {
        return <Image key={index} testID="share-card-photo" source={photoUrl ?? undefined} contentFit="cover" accessibilityElementsHidden
          onLoad={() => onPhotoState("loaded")} onError={() => onPhotoState("failed")}
          style={[box, { borderRadius: 12 * scale, backgroundColor: SHARE_CARD_COLORS.placeholder }]} />;
      }
      return <Text key={index} numberOfLines={item.maxLines} adjustsFontSizeToFit={item.maxLines === 1} minimumFontScale={0.6}
        style={[box, styles.text, { fontSize: item.size * scale, fontWeight: item.weight, color: item.color, lineHeight: (item.maxLines === 1 ? item.height : item.size * 1.25) * scale }]}>
        {item.text}
      </Text>;
    })}
  </View>;
});

const styles = StyleSheet.create({
  card: {
    overflow: "hidden",
  },
  text: {
    textAlign: "center",
  },
});
