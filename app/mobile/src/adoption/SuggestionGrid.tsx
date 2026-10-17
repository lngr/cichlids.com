import { Pressable, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { PhotoSuggestion } from "@cichlids/client-core";
import { useTheme } from "../theme";
import { choicePosition, gridColumns, gridImageUrl, suggestionDate } from "./adoptionView";

/**
 * The suggested photos in a grid with their dates. A tap chooses a photo, and a chosen photo shows its
 * place in the choice; another tap takes it out.
 */
export function SuggestionGrid({ items, chosen, width, onToggle }: {
  items: PhotoSuggestion[];
  chosen: readonly number[];
  width: number;
  onToggle: (mediaItemId: number) => void;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const cell = Math.floor(width / gridColumns(width));
  return <View testID="adoption-grid" style={{ flexDirection: "row", flexWrap: "wrap" }}>
    {items.map(item => {
      const position = choicePosition(chosen, item.mediaItemId);
      const date = suggestionDate(item, i18n.language);
      const label = position === null ? t("adoption.photo", { date }) : t("adoption.photoChosen", { date, position });
      return <Pressable key={item.mediaItemId} testID={`adoption-photo-${item.mediaItemId}`} accessibilityRole="checkbox"
        accessibilityLabel={label} accessibilityState={{ checked: position !== null }} onPress={() => onToggle(item.mediaItemId)}
        style={{ width: cell, padding: theme.space[1] }}>
        <View style={{ borderRadius: theme.radius.md, overflow: "hidden", borderWidth: 2, borderColor: position === null ? theme.colors.border : theme.colors.accent, backgroundColor: theme.colors.surface2 }}>
          <Image source={gridImageUrl(item.image)} accessible={false} contentFit="cover" style={{ width: "100%", aspectRatio: 1 }} />
          {position === null ? null : <View testID={`adoption-position-${item.mediaItemId}`}
            style={{ position: "absolute", top: theme.space[1], start: theme.space[1], minWidth: 24, height: 24, borderRadius: theme.radius.pill, paddingHorizontal: theme.space[1], alignItems: "center", justifyContent: "center", backgroundColor: theme.colors.accent }}>
            <Text style={[theme.type.meta, { color: theme.colors.onAccent, fontWeight: "700" }]}>{position}</Text>
          </View>}
          <Text numberOfLines={1} style={[theme.type.meta, { color: theme.colors.muted, textAlign: "center", paddingVertical: theme.space[1] }]}>{date}</Text>
        </View>
      </Pressable>;
    })}
  </View>;
}
