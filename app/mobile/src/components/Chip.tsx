import { Pressable, StyleSheet, Text } from "react-native";
import { useTheme } from "../theme";

export function Chip({ label, active, onPress }: { label: string; active: boolean; onPress: () => void }) {
  const theme = useTheme();
  return (
    <Pressable
      onPress={onPress}
      style={[
        styles.chip,
        {
          backgroundColor: active ? theme.colors.accent : theme.colors.surface2,
          borderColor: active ? theme.colors.accent : theme.colors.border,
        },
      ]}
      accessibilityRole="button"
      accessibilityState={{ selected: active }}
    >
      <Text style={[styles.label, { color: active ? theme.colors.onAccent : theme.colors.fg }]}>{label}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 999,
    borderWidth: 1,
    minHeight: 36,
    justifyContent: "center",
  },
  label: {
    fontSize: 13,
    fontWeight: "600",
  },
});
