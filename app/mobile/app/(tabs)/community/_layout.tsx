import { Stack } from "expo-router";
import { useTheme } from "../../../src/theme";

export default function CommunityStackLayout() {
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
      <Stack.Screen name="index" options={{ title: "Community" }} />
      <Stack.Screen name="thread/[id]" options={{ title: "" }} />
    </Stack>
  );
}
