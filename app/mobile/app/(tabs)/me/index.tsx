import { useCallback, useEffect, useRef, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect, useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import type { Me } from "@cichlids/client-core";
import { apiClient } from "../../../src/api/client";
import { useAuth } from "../../../src/auth/AuthProvider";
import { useLoginAction } from "../../../src/auth/useLoginAction";
import { Button } from "../../../src/components/Button";
import { ErrorView, LoadingView } from "../../../src/components/StatusView";
import { translateApiError } from "../../../src/lib/apiErrors";
import { useTheme } from "../../../src/theme";
import { ProfileHeader } from "../../../src/profile/ProfileHeader";
import { ProfileTabs, type ProfileSection } from "../../../src/profile/ProfileTabs";
import { ProfileContentList } from "../../../src/profile/ProfileContentList";
import { useProfileLists } from "../../../src/profile/useProfileLists";
import { DraftsSection } from "../../../src/upload/DraftsSection";

export default function MeScreen() {
  const { status, accountId } = useAuth();

  if (status === "loading") return <LoadingView />;
  if (status === "anonymous") return <AnonymousView />;
  return <ProfileView key={accountId} />;
}

function AnonymousView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { busy, failure, onLogin } = useLoginAction();

  return (
    <View style={[styles.anonymous, { backgroundColor: theme.colors.bg, padding: theme.space[6], gap: theme.space[3] }]}>
      <Text style={[theme.type.h1, styles.centered, { color: theme.colors.fg }]}>{t("me.anonymousTitle")}</Text>
      <Text style={[theme.type.body, styles.centered, { color: theme.colors.muted }]}>{t("me.anonymousText")}</Text>
      {failure ? (
        <Text testID="login-error" accessibilityRole="alert" style={[theme.type.body, styles.centered, { color: theme.colors.danger }]}>
          {failure === "unavailable" ? t("me.loginUnavailable") : t("me.loginError")}
        </Text>
      ) : null}
      <Button testID="login-button" label={failure ? t("common.retry") : t("me.login")} onPress={onLogin} busy={busy} />
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
      .catch((err) => !cancelled && setError(translateApiError(err, t, "common.loadError")));
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

  return <MemberContent key={String(me.profile.id)} profile={me.profile} />;
}

function MemberContent({ profile }: { profile: Me["profile"] }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const { logout } = useAuth();
  const router = useRouter();
  const [section, setSection] = useState<ProfileSection>("pictures");
  const { pictures, tanks } = useProfileLists(Number(profile.id));
  const firstFocus = useRef(true);
  useFocusEffect(useCallback(() => {
    if (firstFocus.current) {
      firstFocus.current = false;
      return;
    }
    pictures.reload();
    tanks.reload();
  }, [pictures.reload, tanks.reload]));
  const header = (
    <View>
      <Pressable testID="me-profile-link" accessibilityRole="link" accessibilityLabel={t("me.openProfile")} onPress={() => router.push(`/profile/${profile.id}`)}>
        <ProfileHeader profile={profile} own />
      </Pressable>
      <View style={{ paddingHorizontal: theme.space[4], gap: theme.space[3] }}>
        <Button testID="me-profile-button" variant="secondary" label={t("me.openProfile")} onPress={() => router.push(`/profile/${profile.id}`)} />
        <Button testID="logout-button" variant="secondary" label={t("me.logout")} onPress={() => void logout()} />
      </View>
      <ProfileTabs section={section} onSelect={setSection} own />
    </View>
  );
  if (section === "drafts") {
    return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ paddingBottom: theme.space[6] }}>
      {header}
      <View style={{ paddingHorizontal: theme.space[4] }}><DraftsSection /></View>
    </ScrollView>;
  }
  return <ProfileContentList section={section} pictures={pictures} tanks={tanks} header={header} />;
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
});
