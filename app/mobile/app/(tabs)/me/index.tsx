import { useMachineRouter } from "../../../src/navigation/NavigationProvider";
import { OnboardingProfile } from "../../../src/onboarding/OnboardingProfile";
import { LanguageSettings } from "../../../src/i18n/LanguageSettings";
import { useCallback, useRef, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useFocusEffect } from "expo-router";
import { useTranslation } from "react-i18next";
import type { Me } from "@cichlids/client-core";
import { apiClient } from "../../../src/api/client";
import { useAuth } from "../../../src/auth/AuthProvider";
import { useLoginAction } from "../../../src/auth/useLoginAction";
import { Button } from "../../../src/components/Button";
import { ErrorView, LoadingView } from "../../../src/components/StatusView";
import { translateApiError } from "../../../src/lib/apiErrors";
import { useTheme } from "../../../src/theme";
import { HandleSettings } from "../../../src/profile/HandleSettings";
import { ProfileHeader } from "../../../src/profile/ProfileHeader";
import { ProfileTabs, type ProfileSection } from "../../../src/profile/ProfileTabs";
import { ProfileContentList } from "../../../src/profile/ProfileContentList";
import { useProfileLists } from "../../../src/profile/useProfileLists";
import { DeviceAccountCard } from "../../../src/registration/DeviceAccountCard";
import { StreakSettings } from "../../../src/streaks/StreakSettings";
import { VacationShareLine } from "../../../src/streaks/VacationShareLine";
import { DraftsSection } from "../../../src/upload/DraftsSection";
import { WebPushSettings } from "../../../src/webPush/WebPushSettings";

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
    <ScrollView contentContainerStyle={[styles.anonymous, { backgroundColor: theme.colors.bg, padding: theme.space[6], gap: theme.space[3] }]}>
      <Text style={[theme.type.h1, styles.centered, { color: theme.colors.fg }]}>{t("me.anonymousTitle")}</Text>
      <Text style={[theme.type.body, styles.centered, { color: theme.colors.muted }]}>{t("me.anonymousText")}</Text>
      {failure ? (
        <Text testID="login-error" accessibilityRole="alert" style={[theme.type.body, styles.centered, { color: theme.colors.danger }]}>
          {failure === "unavailable" ? t("me.loginUnavailable") : t("me.loginError")}
        </Text>
      ) : null}
      <LanguageSettings />
      <Button testID="login-button" label={failure ? t("common.retry") : t("me.login")} onPress={onLogin} busy={busy} />
    </ScrollView>
  );
}

function ProfileView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { logout, provisional } = useAuth();
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useFocusEffect(useCallback(() => {
    let cancelled = false;
    setError(null);
    apiClient.me
      .get()
      .then((result) => !cancelled && setMe(result))
      .catch((err) => !cancelled && setError(translateApiError(err, t, "common.loadError")));
    return () => {
      cancelled = true;
    };
  }, [attempt, t]));

  if (error) {
    return (
      <View style={[styles.fill, { backgroundColor: theme.colors.bg }]}>
        <ErrorView message={error} onRetry={() => setAttempt((n) => n + 1)} />
        {provisional ? null : <View style={{ padding: theme.space[4] }}>
          <Button testID="logout-button" variant="secondary" label={t("me.logout")} onPress={() => void logout()} />
        </View>}
      </View>
    );
  }
  if (!me) return <LoadingView label={t("me.loading")} />;

  return <MemberContent key={String(me.profile.id)} profile={me.profile} handleChangeAvailableAt={me.handleChangeAvailableAt}
    archiveRetrospective={Boolean(me.archiveRetrospective?.available)} returnRetrospective={Boolean(me.returnRetrospective?.available)}
    onProfileChanged={() => setAttempt((n) => n + 1)} />;
}

function MemberContent({ profile, handleChangeAvailableAt, archiveRetrospective, returnRetrospective, onProfileChanged }: {
  profile: Me["profile"];
  handleChangeAvailableAt?: string | null;
  archiveRetrospective: boolean;
  returnRetrospective: boolean;
  onProfileChanged: () => void;
}) {
  const theme = useTheme();
  const { t } = useTranslation();
  const { logout, provisional } = useAuth();
  const router = useMachineRouter();
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
      {provisional ? <ProfileHeader profile={profile} own handle={false} /> : (
        <Pressable testID="me-profile-link" accessibilityRole="link" accessibilityLabel={t("me.openProfile")} onPress={() => router.push(`/profile/${profile.id}`)}>
          <ProfileHeader profile={profile} own />
        </Pressable>
      )}
      <DeviceAccountCard />
      <VacationShareLine share={profile.vacationShare} />
      <View style={{ paddingHorizontal: theme.space[4], gap: theme.space[3] }}>
        {provisional ? null : <Button testID="me-profile-button" variant="secondary" label={t("me.openProfile")} onPress={() => router.push(`/profile/${profile.id}`)} />}
        <Button testID="me-todos" variant="secondary" label={t("me.todos")} onPress={() => router.push("/todos")} />
        <Button testID="me-media" variant="secondary" label={t("me.media")} onPress={() => router.push("/media")} />
        {provisional ? null : <>
          <Button testID="me-profile-images" variant="secondary" label={t("me.profileImages")} onPress={() => router.push("/profile-images")} />
          <Button testID="me-stories" variant="secondary" label={t("me.stories")} onPress={() => router.push("/stories")} />
          {archiveRetrospective ? <Button testID="me-archive-retrospective" variant="secondary" label={t("me.archiveRetrospective")} onPress={() => router.push("/retrospective/archive")} /> : null}
          {returnRetrospective ? <Button testID="me-return-retrospective" variant="secondary" label={t("me.returnRetrospective")} onPress={() => router.push("/retrospective/return")} /> : null}
          <Button testID="logout-button" variant="secondary" label={t("me.logout")} onPress={() => void logout()} />
        </>}
      </View>
      <LanguageSettings />
      {provisional ? null : <View style={{ paddingHorizontal: theme.space[4] }}>
        <HandleSettings handle={profile.username} changeAvailableAt={handleChangeAvailableAt} onChanged={onProfileChanged} />
      </View>}
      {provisional ? null : <View style={{ paddingHorizontal: theme.space[4] }}><WebPushSettings /></View>}
      <View style={{ paddingHorizontal: theme.space[4] }}><StreakSettings onChanged={onProfileChanged} /></View>
      {provisional ? null : <OnboardingProfile profile={profile} />}
      <ProfileTabs section={section} onSelect={setSection} own drafts={!provisional} />
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
    flexGrow: 1,
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
