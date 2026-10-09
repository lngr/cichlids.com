import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { useTranslation } from "react-i18next";
import { useTheme } from "../src/theme";
import { AuthProvider } from "../src/auth/AuthProvider";
import "../src/i18n";

export default function RootLayout() {
  const theme = useTheme();
  const { t } = useTranslation();

  return (
    <AuthProvider>
      <StatusBar style={theme.scheme === "dark" ? "light" : "dark"} />
      <Stack
        screenOptions={{
          headerStyle: { backgroundColor: theme.colors.surface },
          headerTintColor: theme.colors.fg,
          headerShadowVisible: false,
          contentStyle: { backgroundColor: theme.colors.bg },
        }}
      >
        <Stack.Screen name="index" options={{ headerShown: false }} />
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="profile/[id]" options={{ title: t("profile.title") }} />
        <Stack.Screen name="auth" options={{ headerShown: false }} />
        <Stack.Screen name="upload/index" options={{ title: t("upload.title") }} />
      </Stack>
    </AuthProvider>
  );
}
