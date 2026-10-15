import { View, Platform, AppState } from "react-native";
import { LocaleDirContext } from "expo-router/react-navigation";
import { useEffect, useState, useSyncExternalStore } from "react";
import { Stack } from "expo-router";
import { StatusBar } from "expo-status-bar";
import { useTranslation } from "react-i18next";
import { useTheme } from "../src/theme";
import { AuthProvider } from "../src/auth/AuthProvider";
import { initializeLocalization, subscribeDirection, getDirection, refreshDeviceLanguage } from "../src/i18n";
import { FeatureFlagsProvider } from "../src/featureFlags/FeatureFlagsProvider";

export default function RootLayout() {
  const [ready, setReady] = useState(false);
  useEffect(() => { void initializeLocalization().then(() => setReady(true)); }, []);
  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") void refreshDeviceLanguage();
    });
    return () => subscription.remove();
  }, []);
  const rtl = useSyncExternalStore(subscribeDirection, getDirection, () => false);
  const direction = rtl ? "rtl" : "ltr";
  const theme = useTheme();
  const { t } = useTranslation();

  if (!ready) return null;

  return (
    <LocaleDirContext.Provider value={direction}>
      <View {...(Platform.OS === "web" ? { dir: direction } : {})} style={{ flex: 1, direction }}>
        <AuthProvider>
          <FeatureFlagsProvider>
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
          </FeatureFlagsProvider>
        </AuthProvider>
      </View>
    </LocaleDirContext.Provider>
  );
}
