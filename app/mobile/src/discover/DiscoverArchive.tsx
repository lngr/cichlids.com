import { useFocusEffect } from "expo-router";
import { useCallback, useState } from "react";
import { Image } from "expo-image";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { DiscoverArchive as Archive, DiscoverItem, TankRegion } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { orderDiscoverRegions } from "./regions";

export function DiscoverArchive() {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const { status, accountId } = useAuth();
  const [interests, setInterests] = useState<string[]>([]);
  const [selected, setSelected] = useState<TankRegion>("lake_malawi");
  const [archive, setArchive] = useState<Archive | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useFocusEffect(useCallback(() => {
    let active = true;
    setInterests([]);
    if (status === "authenticated") void apiClient.me.get().then(me => {
      if (!active) return;
      const interests = me.profile.interests ?? [];
      setInterests(interests);
      setSelected(orderDiscoverRegions(interests)[0]);
    }).catch(() => { if (active) setInterests([]); });
    return () => { active = false; };
  }, [status, accountId]));
  useFocusEffect(useCallback(() => {
    let active = true;
    setFailed(false);
    void apiClient.discover.get([selected]).then(result => { if (active) setArchive(result); })
      .catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [selected, attempt]));
  const region = archive?.regions.find(region => region.region === selected);
  const empty = (id: string, key: string) => <Text testID={`${id}-empty`} style={[theme.type.body, { color: theme.colors.muted }]}>
    {t(key, { region: t(`regions.${selected}`) })}
  </Text>;
  const card = (item: DiscoverItem, picture = false, hero = false) => {
    const title = item.title || t("common.untitled");
    const image = item.image?.medium ?? item.image?.small ?? item.image?.thumb ?? item.image?.large;
    return <Pressable key={String(item.id)} accessibilityRole="button" accessibilityLabel={title}
      onPress={() => router.push(picture ? `/gallery/${item.slug ?? item.id}` : `/tanks/${item.id}`)}
      style={{ minHeight: 48, minWidth: 48, width: hero ? "100%" : 240, overflow: "hidden", borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>
      <View style={{ aspectRatio: hero ? 16 / 9 : 4 / 3, backgroundColor: theme.colors.placeholder }}>
        {image ? <Image source={image} accessibilityLabel={title} style={{ width: "100%", height: "100%" }} contentFit="cover" /> : null}
      </View>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, padding: theme.space[3] }]}>{title}</Text>
    </Pressable>;
  };
  const shelf = (id: string, title: string, items: DiscoverItem[], pictures = false, emptyKey = "discover.empty") => <View testID={id} style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(title)}</Text>
    {items.length ? <ScrollView horizontal contentContainerStyle={{ gap: theme.space[3] }}>{items.map(item => card(item, pictures))}</ScrollView>
      : empty(id, emptyKey)}
  </View>;
  return <View style={{ gap: theme.space[6] }}>
    <Text accessibilityRole="header" style={[theme.type.display, { color: theme.colors.fg }]}>{t("discover.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("discover.intro")}</Text>
    <View accessibilityRole="radiogroup" accessibilityLabel={t("onboarding.region")} style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
      {orderDiscoverRegions(interests).map(region => <Pressable key={region} testID={`discover-region-${region}`}
        accessibilityRole="radio" accessibilityLabel={t(`regions.${region}`)} accessibilityState={{ selected: selected === region }} onPress={() => setSelected(region)}
        style={{ minHeight: 48, minWidth: 48, justifyContent: "center", paddingHorizontal: theme.space[4], paddingVertical: theme.space[2],
          borderRadius: theme.radius.pill, borderWidth: 1, borderColor: selected === region ? theme.colors.fg : theme.colors.border,
          backgroundColor: selected === region ? theme.colors.surface2 : theme.colors.surface }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`regions.${region}`)}</Text>
      </Pressable>)}
    </View>
    {failed ? <View accessibilityRole="alert" style={{ gap: theme.space[3] }}>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("common.loadError")}</Text>
      <Button variant="secondary" label={t("common.retry")} onPress={() => setAttempt(value => value + 1)} />
    </View> : !region ? <Text accessibilityLiveRegion="polite" style={[theme.type.body, { color: theme.colors.muted }]}>{t("common.loading")}</Text> : <>
      <View testID="discover-week" style={{ gap: theme.space[3] }}>
        <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("discover.week")}</Text>
        {region.tankOfTheWeek ? card(region.tankOfTheWeek, false, true) : empty("discover-week", "discover.weekEmpty")}
      </View>
      {shelf("discover-hall-tanks", "discover.tanks", region.tanks)}
      {shelf("discover-hall-pictures", "discover.pictures", region.pictures, true)}
      {shelf("discover-showcases", "discover.showcases", region.timeLapses)}
      {shelf("discover-global-pictures", "discover.globalPictures", archive?.globalPictures ?? [], true, "discover.globalEmpty")}
    </>}
  </View>;
}
