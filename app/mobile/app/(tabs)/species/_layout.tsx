import { Stack } from "expo-router";
import { useTheme } from "../../../src/theme";

export default function SpeciesStackLayout() {
  const theme = useTheme();
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: theme.colors.surface },
        headerTintColor: theme.colors.fg,
        headerShadowVisible: false,
        contentStyle: { backgroundColor: theme.colors.bg },
      }}
    >
      <Stack.Screen name="index" options={{ title: "Arten" }} />
      <Stack.Screen name="[idOrSlug]" options={{ title: "" }} />
    </Stack>
  );
}
