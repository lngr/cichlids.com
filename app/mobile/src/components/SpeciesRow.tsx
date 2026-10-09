import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTheme } from "../theme";
import type { SpeciesListItem } from "@cichlids/client-core";

export function SpeciesRow({ species, onPress }: { species: SpeciesListItem; onPress: () => void }) {
  const theme = useTheme();
  return (
    <Pressable style={[styles.row, { borderColor: theme.colors.border }]} onPress={onPress} accessibilityRole="button">
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, fontStyle: "italic" }]}>{species.displayName}</Text>
      {species.category ? (
        <View style={[styles.badge, { backgroundColor: theme.colors.accentWeak }]}>
          <Text style={[theme.type.meta, { color: theme.colors.accent }]}>{species.category}</Text>
        </View>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingVertical: 12,
    paddingHorizontal: 16,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  badge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 999,
  },
});
