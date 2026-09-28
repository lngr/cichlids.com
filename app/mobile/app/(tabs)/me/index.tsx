import { useEffect, useState } from "react";
import { Image, ScrollView, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { Me } from "@cichlids/client-core";
import { apiClient } from "../../../src/api/client";
import { useAuth } from "../../../src/auth/AuthProvider";
import { Button } from "../../../src/components/Button";
import { ErrorView, LoadingView } from "../../../src/components/StatusView";
import { useTheme } from "../../../src/theme";
import { DraftsSection } from "../../../src/upload/DraftsSection";

export default function MeScreen() {
  const { status } = useAuth();

  if (status === "loading") return <LoadingView />;
  if (status === "anonymous") return <AnonymousView />;
  return <ProfileView />;
}

function AnonymousView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { login, loginReady } = useAuth();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  // login() is called directly from the press handler: on the web it opens the Keycloak popup,
  // which browsers only allow as the immediate result of a user gesture.
  const onLogin = () => {
    setFailed(false);
    setBusy(true);
    login()
      .catch(() => setFailed(true))
      .finally(() => setBusy(false));
  };

  return (
    <View style={[styles.anonymous, { backgroundColor: theme.colors.bg, padding: theme.space[6], gap: theme.space[3] }]}>
      <Text style={[theme.type.h1, styles.centered, { color: theme.colors.fg }]}>{t("me.anonymousTitle")}</Text>
      <Text style={[theme.type.body, styles.centered, { color: theme.colors.muted }]}>{t("me.anonymousText")}</Text>
      {failed ? <Text style={[theme.type.body, styles.centered, { color: theme.colors.danger }]}>{t("me.loginError")}</Text> : null}
      <Button testID="login-button" label={t("me.login")} onPress={onLogin} disabled={!loginReady} busy={busy} />
    </View>
  );
}

function ProfileView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { logout } = useAuth();
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    apiClient.me
      .get()
      .then((result) => !cancelled && setMe(result))
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : t("common.loadError")));
    return () => {
      cancelled = true;
    };
  }, [attempt, t]);

  if (error) {
    return (
      <View style={[styles.fill, { backgroundColor: theme.colors.bg }]}>
        <ErrorView message={error} onRetry={() => setAttempt((n) => n + 1)} />
        <View style={{ padding: theme.space[4] }}>
          <Button testID="logout-button" variant="secondary" label={t("me.logout")} onPress={() => void logout()} />
        </View>
      </View>
    );
  }
  if (!me) return <LoadingView label={t("me.loading")} />;

  const { profile } = me;
  return (
    <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[6] }}>
      <View style={[styles.headerRow, { gap: theme.space[3] }]}>
        {profile.avatarUrl ? (
          <Image source={{ uri: profile.avatarUrl }} style={[styles.avatar, { backgroundColor: theme.colors.placeholder }]} />
        ) : (
          <View style={[styles.avatar, { backgroundColor: theme.colors.placeholder }]} />
        )}
        <View style={styles.fill}>
          <Text testID="me-name" style={[theme.type.h1, { color: theme.colors.fg }]}>
            {profile.displayName ?? profile.username}
          </Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>@{profile.username}</Text>
        </View>
      </View>
      <DraftsSection />
      <Button testID="logout-button" variant="secondary" label={t("me.logout")} onPress={() => void logout()} />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  anonymous: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  centered: {
    textAlign: "center",
  },
  fill: {
    flex: 1,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
  },
  avatar: {
    width: 64,
    height: 64,
    borderRadius: 32,
  },
});
