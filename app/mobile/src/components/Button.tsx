import { ActivityIndicator, Pressable, StyleSheet, Text } from "react-native";
import { minTouchTarget, useTheme } from "../theme";

/** A full-width action button; "primary" uses the accent surface, "secondary" a neutral one. */
export function Button({
  label,
  onPress,
  variant = "primary",
  disabled = false,
  busy = false,
  testID,
}: {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary";
  disabled?: boolean;
  busy?: boolean;
  testID?: string;
}) {
  const theme = useTheme();
  const primary = variant === "primary";
  const inactive = disabled || busy;
  const textColor = primary ? theme.colors.onAccent : theme.colors.fg;
  return (
    <Pressable
      testID={testID}
      onPress={onPress}
      disabled={inactive}
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy }}
      style={({ pressed }) => [
        styles.button,
        {
          backgroundColor: primary ? theme.colors.accent : theme.colors.surface2,
          borderColor: primary ? theme.colors.accent : theme.colors.border,
          borderRadius: theme.radius.md,
          paddingHorizontal: theme.space[4],
          opacity: inactive ? 0.6 : pressed ? 0.85 : 1,
        },
      ]}
    >
      {busy ? <ActivityIndicator color={textColor} /> : <Text style={[theme.type.bodyStrong, { color: textColor }]}>{label}</Text>}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: minTouchTarget,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center",
    alignSelf: "stretch",
  },
});
