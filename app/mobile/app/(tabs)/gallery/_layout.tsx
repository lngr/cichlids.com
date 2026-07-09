import { Stack } from "expo-router";
import { useTheme } from "../../../src/theme";

export default function GalleryStackLayout() {
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
      <Stack.Screen name="index" options={{ title: "Galerie" }} />
      <Stack.Screen name="[slug]" options={{ title: "" }} />
    </Stack>
  );
}
