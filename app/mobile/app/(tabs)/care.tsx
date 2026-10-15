import { useCallback, useEffect, useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useLocalSearchParams } from "expo-router";
import type { TankDetail, TankListItem } from "@cichlids/client-core";
import { apiClient } from "../../src/api/client";
import { translateApiError } from "../../src/lib/apiErrors";
import { useTheme } from "../../src/theme";
import { useAuth } from "../../src/auth/AuthProvider";
import { useMachineRouter } from "../../src/navigation/NavigationProvider";
import { usePagedList } from "../../src/hooks/usePagedList";
import { Button } from "../../src/components/Button";
import { CareStreamView } from "../../src/tanks/CareStreamView";
import { useCareStream } from "../../src/tanks/useCareStream";

export default function CarePicker() {
  const { tank: selected } = useLocalSearchParams<{ tank?: string }>();
  const { status, accountId } = useAuth();
  return <CarePickerContent key={`${accountId}:${status}:${selected ?? ""}`} selected={selected} />;
}
function CarePickerContent({ selected }: { selected?: string }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [profileId, setProfileId] = useState<number | null>(null);
  const { status } = useAuth();
  useEffect(() => {
    let cancelled = false;
    if (status === "authenticated") void apiClient.me.get().then(me => { if (!cancelled) setProfileId(Number(me.profile.id)); }).catch(() => {});
    return () => { cancelled = true; };
  }, [status]);
  const fetchPage = useCallback((offset: number, limit: number) => profileId === null ? Promise.resolve({ total: 0, items: [] })
    : apiClient.tanks.list({ user: profileId, offset, limit }), [profileId]);
  const list = usePagedList<TankListItem>(fetchPage, [profileId]);
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("care.waterChange")}</Text>
    {selected && profileId !== null ? <QuickCare tankId={Number(selected)} profileId={profileId} /> : <>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("care.chooseTank")}</Text>
      {list.items.map(tank => <Button key={String(tank.id)} testID={`care-choose-${tank.id}`} variant="secondary" label={tank.title} onPress={() => router.push(`/care?tank=${tank.id}`)} />)}
      {list.error ? <Button variant="secondary" label={t("common.retry")} onPress={list.reload} /> : null}
      {profileId !== null && !list.loading && !list.error && list.items.length === 0 ? <>
        <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("care.noTanks")}</Text>
        <Button label={t("today.createTank")} onPress={() => router.push("/onboarding")} />
      </> : null}
      {list.hasMore ? <Button variant="secondary" label={t("care.more")} busy={list.loadingMore} onPress={list.loadMore} /> : null}
    </>}
    <View style={{ height: theme.space[4] }} />
  </ScrollView>;
}
function QuickCare({ tankId, profileId }: { tankId: number; profileId: number }) {
  const [tank, setTank] = useState<TankDetail | null>(null);
  const [error, setError] = useState<unknown>(null);
  const { t } = useTranslation();
  const theme = useTheme();
  useEffect(() => {
    let cancelled = false;
    void apiClient.tanks.get(tankId).then(value => {
      if (!cancelled) setTank(value);
    }).catch(err => { if (!cancelled) setError(err); });
    return () => { cancelled = true; };
  }, [tankId]);
  if (error) return <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(error, t, "common.loadError")}</Text>;
  if (!tank) return <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("common.loading")}</Text>;
  if (String(tank.author.id) !== String(profileId)) return <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("errors.care_not_found")}</Text>;
  return <View style={{ gap: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{tank.title}</Text>
    <OwnQuickCare tankId={tankId} profileId={profileId} />
  </View>;
}
function OwnQuickCare({ tankId, profileId }: { tankId: number; profileId: number }) {
  const stream = useCareStream(tankId, profileId);
  const { t } = useTranslation();
  const router = useMachineRouter();
  return <View testID="care-quick-log" style={{ gap: 16 }}>
    <CareStreamView stream={stream} />
    <Button testID="care-open-tank" variant="secondary" label={t("care.openTank")} onPress={() => router.push(`/tanks/${tankId}?view=care`)} />
  </View>;
}
