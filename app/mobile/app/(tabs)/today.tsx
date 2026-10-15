import { useCallback, useState } from "react";
import { useFocusEffect } from "expo-router";
import { Image } from "expo-image";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { Today } from "@cichlids/client-core";
import { apiClient } from "../../src/api/client";
import { useAuth } from "../../src/auth/AuthProvider";
import { useLoginAction } from "../../src/auth/useLoginAction";
import { Button } from "../../src/components/Button";
import { ErrorView, LoadingView } from "../../src/components/StatusView";
import { translateApiError } from "../../src/lib/apiErrors";
import { useMachineRouter } from "../../src/navigation/NavigationProvider";
import { minTouchTarget, useTheme } from "../../src/theme";
import { DueCareSection } from "../../src/today/DueCareSection";
import { localCalendarDate } from "../../src/today/localDate";
import { draftTitle } from "../../src/upload/drafts";

type ImageUrls = NonNullable<Today["onThisDay"]>["image"];

export default function TodayScreen() {
  const { status, accountId } = useAuth();
  if (status === "loading") return <LoadingView />;
  return status === "authenticated" ? <MemberToday key={accountId} /> : <Welcome />;
}

function Welcome() {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const { busy, failure, onLogin } = useLoginAction();
  return <ScrollView testID="today-welcome" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: theme.space[6], gap: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.display, { color: theme.colors.fg }]}>{t("today.welcome")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("today.intro")}</Text>
    {failure ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t(failure === "unavailable" ? "me.loginUnavailable" : "me.loginError")}</Text> : null}
    <Button testID="today-login" label={t(failure ? "common.retry" : "me.login")} onPress={onLogin} busy={busy} />
    <Button testID="today-discover" variant="secondary" label={t("today.discover")} onPress={() => router.push("/discover")} />
  </ScrollView>;
}

/** The place of the care streak in the Today layout, between the greeting and the due care. */
function StreakSlot() {
  return null;
}

function MemberToday() {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const [today, setToday] = useState<Today | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useFocusEffect(useCallback(() => {
    let active = true;
    setError(null);
    void apiClient.me.today(localCalendarDate()).then(result => { if (active) setToday(result); })
      .catch(err => { if (active) setError(translateApiError(err, t, "common.loadError")); });
    return () => { active = false; };
  }, [attempt, t]));
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!today) return <LoadingView />;

  const heading = (label: string) => <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{label}</Text>;
  const card = (id: string, title: string, image: ImageUrls | null | undefined, path: string) => {
    const uri = image?.medium ?? image?.small ?? image?.large ?? image?.thumb;
    return <Pressable key={id} testID={id} accessibilityRole="link" accessibilityLabel={title} onPress={() => router.push(path)}
      style={({ pressed }) => ({ minWidth: minTouchTarget, minHeight: minTouchTarget, borderRadius: theme.radius.lg, overflow: "hidden", backgroundColor: theme.colors.surface, opacity: pressed ? 0.85 : 1 })}>
      {uri ? <Image source={uri} accessibilityLabel={title} contentFit="cover" style={{ width: "100%", aspectRatio: 16 / 9, backgroundColor: theme.colors.placeholder }} /> : null}
      <Text style={[theme.type.bodyStrong, { minHeight: minTouchTarget, color: theme.colors.fg, padding: theme.space[4] }]}>{title}</Text>
    </Pressable>;
  };
  const memory = today.onThisDay;
  const weekly = today.tankOfTheWeek;
  return <ScrollView style={{ flex: 1, backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], paddingBottom: theme.space[8] }}>
    <View testID="today-member" style={{ gap: theme.space[6] }}>
      <View testID="today-greeting" style={{ gap: theme.space[2] }}>
        <Text accessibilityRole="header" style={[theme.type.display, { color: theme.colors.fg }]}>{t("today.greeting", { name: today.displayName })}</Text>
        {today.memberSinceYear != null ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("today.memberSince", { year: today.memberSinceYear })}</Text> : null}
      </View>
      <StreakSlot />
      <DueCareSection />
      {today.tanks.length ? <View testID="today-tanks" style={{ gap: theme.space[3] }}>
        {heading(t("today.tanks"))}
        {today.tanks.map(tank => card(`today-tank-${tank.id}`, tank.title, tank.newestPhoto?.image, `/tanks/${tank.id}`))}
      </View> : <Button testID="today-create-tank" label={t("today.createTank")} onPress={() => router.push("/onboarding")} />}
      {Number(today.draftCount) > 0 ? <View testID="today-drafts" style={{ gap: theme.space[3] }}>
        {heading(t("today.continueEditing"))}
        <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("today.drafts", { count: Number(today.draftCount) })}</Text>
        {today.drafts.map(draft => card(`today-draft-${draft.id}`, draftTitle(draft) ?? t("me.drafts.untitled"), draft.image, `/upload?draft=${draft.id}`))}
      </View> : null}
      {memory ? <View testID="today-memory" style={{ gap: theme.space[3] }}>
        {heading(t("today.onThisDay", { count: Number(memory.yearsAgo) }))}
        {card("today-memory-photo", memory.title || t("common.untitled"), memory.image, `/gallery/${memory.slug ?? memory.postId}`)}
      </View> : null}
      {weekly ? <View testID="today-week" style={{ gap: theme.space[3] }}>
        {heading(t("discover.week"))}
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t(`regions.${weekly.region}`)}</Text>
        {card("today-week-tank", weekly.tank.title || t("common.untitled"), weekly.tank.image, `/tanks/${weekly.tank.id}`)}
      </View> : null}
      <Button testID="today-discover" variant="secondary" label={t("today.discover")} onPress={() => router.push("/discover")} />
    </View>
  </ScrollView>;
}
