import { StyleSheet, Text, View } from "react-native";
import { useTheme } from "../theme";

export function DataRow({ label, value }: { label: string; value: string | null | undefined }) {
  const theme = useTheme();
  if (!value) return null;
  return (
    <View style={[styles.row, { borderColor: theme.colors.border }]}>
      <Text style={[theme.type.meta, { color: theme.colors.muted, width: 110 }]}>{label}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg, flex: 1 }]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    paddingVertical: 6,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
