import { StyleSheet, TextInput } from "react-native";
import { useTheme } from "../theme";

export function SearchField({ value, onChangeText, placeholder }: { value: string; onChangeText: (text: string) => void; placeholder: string }) {
  const theme = useTheme();
  return (
    <TextInput
      value={value}
      onChangeText={onChangeText}
      placeholder={placeholder}
      placeholderTextColor={theme.colors.muted}
      style={[
        styles.input,
        {
          backgroundColor: theme.colors.surface2,
          borderColor: theme.colors.border,
          color: theme.colors.fg,
          borderRadius: theme.radius.md,
        },
      ]}
      autoCorrect={false}
      autoCapitalize="none"
      accessibilityLabel={placeholder}
    />
  );
}

const styles = StyleSheet.create({
  input: {
    marginHorizontal: 12,
    marginTop: 10,
    paddingHorizontal: 14,
    minHeight: 48,
    borderWidth: 1,
    fontSize: 15,
  },
});
