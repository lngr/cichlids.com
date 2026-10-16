import { useEffect, useRef, useState } from "react";
import { AccessibilityInfo, Animated, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import type { LegacyTankDto, OnboardingRequest, OnboardingResultDto } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { RegionChoices } from "./RegionChoices";
import { interestRegions, standardTanks, standardTankRequest, type SelectableRegion } from "./standardTanks";

export default function OnboardingScreen() {
  const { status } = useAuth();
  const router = useMachineRouter();
  const { t } = useTranslation();
  useEffect(() => { if (status === "anonymous") router.replace("/me"); }, [status, router]);
  return status === "authenticated" ? <OnboardingFlow /> : <LoadingView label={t("me.loading")} />;
}

function OnboardingFlow() {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const units = useUnits();
  const router = useMachineRouter();
  const [path, setPath] = useState<"question" | "visibility" | "yes" | "no">("question");
  const [isPublic, setIsPublic] = useState<boolean | null>(null);
  const [legacy, setLegacy] = useState<LegacyTankDto[] | null>(null);
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [legacyTank, setLegacyTank] = useState<LegacyTankDto | null>(null);
  const [standardId, setStandardId] = useState<string | null>(null);
  const [custom, setCustom] = useState(false);
  const [title, setTitle] = useState("");
  const [region, setRegion] = useState<SelectableRegion | null>(null);
  const [interests, setInterests] = useState<SelectableRegion[]>([]);
  const [measurements, setMeasurements] = useState({ volume: "", width: "", depth: "", height: "" });
  const [busy, setBusy] = useState(false);
  const saving = useRef(false);
  const [error, setError] = useState<string | null>(null);
  const [reward, setReward] = useState<OnboardingResultDto | null>(null);
  const standard = standardTanks.find(tank => tank.id === standardId);
  const hasLegacy = legacy !== null && legacy.length > 0;
  const legacyKnown = legacy !== null || loadError !== null;
  const chooseNewTank = (choose: () => void) => { choose(); if (isPublic === null) setPath("visibility"); };
  const number = (value: string) => Number(value.trim().replace(",", "."));
  useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    void apiClient.me.legacyTanks().then(tanks => { if (!cancelled) setLegacy(tanks); })
      .catch(err => { if (!cancelled) setLoadError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  }, [loadAttempt, t]);

  async function save(skip = false) {
    if (saving.current) return;
    let request: OnboardingRequest;
    if (skip) request = { skip: true };
    else if (path === "no") request = { interests };
    else if (legacyTank) request = { legacyTankId: legacyTank.id, region };
    else {
      if (!region || isPublic === null) return;
      if (standard && !custom) request = { tank: { ...standardTankRequest(standard, title, region), isPublic } };
      else request = { tank: { title, region, isPublic,
        volume: { value: number(measurements.volume), unit: units === "us" ? "us_gallon" : "liter" },
        dimensions: { width: number(measurements.width), depth: number(measurements.depth), height: number(measurements.height), unit: units === "us" ? "inch" : "cm" },
      } };
    }
    saving.current = true;
    setBusy(true); setError(null);
    try {
      const result = await apiClient.me.completeOnboarding(request);
      if (result.tankId) setReward(result);
      else router.replace("/me");
    } catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); saving.current = false; }
  }

  const validCustom = Object.values(measurements).every(value => number(value) > 0 && number(value) <= 1000000);
  const canSave = path === "no" || (path === "yes" && (legacyTank
    ? legacyTank.region === "americas_unassigned" ? region === "central_america" || region === "south_america" : Boolean(legacyTank.region || region)
    : isPublic !== null && title.trim().length > 0 && region && (custom ? validCustom : standard)));
  const textStyle = [theme.type.body, { color: theme.colors.fg }];
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[4], flexGrow: 1 }} keyboardShouldPersistTaps="handled">
    {reward ? <Reward reward={reward} /> : <>
      <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t(path === "question" ? "onboarding.question" : path === "visibility" ? "onboarding.visibility.title" : path === "yes" ? "onboarding.tankTitle" : "onboarding.interests")}</Text>
      {path === "question" ? <>
        <Text style={textStyle}>{t("onboarding.intro")}</Text>
        <Button testID="onboarding-yes" label={t("onboarding.yes")} disabled={!legacyKnown} onPress={() => setPath(isPublic === null && !hasLegacy ? "visibility" : "yes")} />
        <Button testID="onboarding-no" variant="secondary" label={t("onboarding.no")} onPress={() => setPath("no")} />
      </> : path === "visibility" ? <>
        <Text style={textStyle}>{t("onboarding.visibility.explain")}</Text>
        <Button testID="onboarding-visibility-public" variant="secondary" label={t("onboarding.visibility.yes")} onPress={() => { setIsPublic(true); setPath("yes"); }} />
        <Button testID="onboarding-visibility-private" variant="secondary" label={t("onboarding.visibility.no")} onPress={() => { setIsPublic(false); setPath("yes"); }} />
        <Button variant="secondary" label={t("navigation.back")} onPress={() => setPath(hasLegacy ? "yes" : "question")} />
      </> : path === "no" ? <RegionChoices multiple regions={interestRegions} selected={interests} onChange={value => setInterests(values => values.includes(value) ? values.filter(v => v !== value) : [...values, value])} /> : <>
        {!legacyTank && isPublic !== null ? <View style={{ gap: theme.space[2] }}>
          <Text testID="onboarding-visibility-summary" style={textStyle}>{t(isPublic ? "onboarding.visibility.summaryPublic" : "onboarding.visibility.summaryPrivate")}</Text>
          <Button testID="onboarding-visibility-change" variant="secondary" label={t("onboarding.visibility.change")} onPress={() => setPath("visibility")} />
        </View> : null}
        {loadError ? <ErrorView message={loadError} onRetry={() => setLoadAttempt(value => value + 1)} /> : legacy === null ? <LoadingView /> : legacy.length > 0 ? <>
          <Text style={[theme.type.h2, { color: theme.colors.fg }]}>{t("onboarding.legacy")}</Text>
          {legacy.map(tank => <Choice key={String(tank.id)} testID={`legacy-tank-${tank.id}`} selected={legacyTank?.id === tank.id} label={tank.title} onPress={() => {
            setLegacyTank(tank); setCustom(false); setStandardId(null);
            setRegion(tank.region && tank.region !== "americas_unassigned" ? tank.region : null);
          }} />)}
        </> : null}
        <Text style={[theme.type.h2, { color: theme.colors.fg }]}>{t("onboarding.standard")}</Text>
        {standardTanks.filter(tank => tank.system === units).map(tank => <Choice key={tank.id} testID={`standard-${tank.id}`} selected={!legacyTank && !custom && standardId === tank.id}
          label={t("onboarding.size", { volume: new Intl.NumberFormat(i18n.language).format(tank.volume), volumeUnit: t(`units.symbols.volume.${units}`), variant: tank.variant ? t(`onboarding.${tank.variant}`) : "", width: tank.width, depth: tank.depth, height: tank.height, lengthUnit: t(`units.symbols.length.${units}`) })}
          onPress={() => chooseNewTank(() => { setStandardId(tank.id); setLegacyTank(null); setCustom(false); })} />)}
        <Choice testID="onboarding-custom" selected={custom} label={t("onboarding.custom")} onPress={() => chooseNewTank(() => { setCustom(true); setLegacyTank(null); setStandardId(null); })} />
        {!legacyTank ? isPublic === null ? null : <>
          <Field testID="onboarding-title" label={t("onboarding.name")} value={title} onChange={setTitle} />
          {custom ? <>
            <Text style={textStyle}>{t("onboarding.customUnits", { volumeUnit: t(`units.symbols.volume.${units}`), lengthUnit: t(`units.symbols.length.${units}`) })}</Text>
            {(["volume", "width", "depth", "height"] as const).map(key => <Field key={key} testID={`onboarding-${key}`} label={t(`onboarding.${key}`)} numeric value={measurements[key]} onChange={value => setMeasurements(values => ({ ...values, [key]: value }))} />)}
          </> : null}
          <Text style={textStyle}>{t("onboarding.region")}</Text>
          <RegionChoices selected={region ? [region] : []} onChange={setRegion} />
        </> : legacyTank.region === "americas_unassigned" || !legacyTank.region ? <>
          <Text style={textStyle}>{t("onboarding.refine")}</Text>
          <RegionChoices regions={legacyTank.region === "americas_unassigned" ? ["central_america", "south_america"] : undefined} selected={region ? [region] : []} onChange={setRegion} />
        </> : <Text style={textStyle}>{t(`regions.${legacyTank.region}`)}</Text>}
      </>}
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      {path === "yes" || path === "no" ? <>
        <Button testID="onboarding-save" label={t("onboarding.save")} busy={busy} disabled={!canSave} onPress={() => void save()} />
        <Button variant="secondary" label={t("navigation.back")} disabled={busy} onPress={() => setPath(path === "yes" && !hasLegacy ? "visibility" : "question")} />
      </> : null}
      <Button testID="onboarding-skip" variant="secondary" label={t("onboarding.skip")} busy={busy} onPress={() => void save(true)} />
    </>}
  </ScrollView>;
}

function Choice({ testID, selected, label, onPress }: { testID: string; selected: boolean; label: string; onPress: () => void }) {
  const theme = useTheme();
  return <Pressable testID={testID} accessibilityRole="radio" accessibilityState={{ selected }} onPress={onPress}
    style={{ minHeight: 48, justifyContent: "center", padding: theme.space[3], borderWidth: selected ? 2 : 1, borderColor: selected ? theme.colors.fg : theme.colors.border, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md }}>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{label}</Text>
  </Pressable>;
}
function Field({ testID, label, value, onChange, numeric = false }: { testID: string; label: string; value: string; onChange: (value: string) => void; numeric?: boolean }) {
  const theme = useTheme();
  return <View style={{ gap: theme.space[2] }}>
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{label}</Text>
    <TextInput testID={testID} accessibilityLabel={label} value={value} onChangeText={onChange} keyboardType={numeric ? "decimal-pad" : "default"} maxLength={numeric ? 12 : 200}
      style={[theme.type.body, { minHeight: 48, padding: theme.space[3], color: theme.colors.fg, borderColor: theme.colors.border, borderWidth: 1, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }]} />
  </View>;
}
function Reward({ reward }: { reward: OnboardingResultDto }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const opacity = useRef(new Animated.Value(1)).current;
  useEffect(() => {
    let cancelled = false;
    void AccessibilityInfo.isReduceMotionEnabled().then(reduced => {
      if (reduced || cancelled) return;
      opacity.setValue(0.55);
      Animated.timing(opacity, { toValue: 1, duration: 600, useNativeDriver: true }).start();
    }).catch(() => {});
    return () => { cancelled = true; opacity.stopAnimation(); };
  }, [opacity]);
  return <View testID="onboarding-reward" style={{ flex: 1, justifyContent: "center", gap: theme.space[6] }}>
    <Animated.View style={{ opacity, alignItems: "center", padding: theme.space[6], backgroundColor: theme.colors.surface, borderRadius: theme.radius.lg, gap: theme.space[4] }}>
      <View style={{ width: 200, height: 120, borderWidth: 2, borderColor: theme.colors.border, borderRadius: theme.radius.md, alignItems: "center", justifyContent: "center", backgroundColor: theme.colors.accentWeak }}><MaterialCommunityIcons name="fish" size={64} color={theme.colors.accent} accessibilityElementsHidden importantForAccessibility="no" /></View>
      <Text style={[theme.type.h1, { color: theme.colors.fg, textAlign: "center" }]}>{reward.title}</Text>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg, textAlign: "center" }]}>{t("onboarding.success")}</Text>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`badges.${reward.badge}`)}</Text>
    </Animated.View>
    <Button testID="onboarding-photo" label={t("onboarding.photo")} onPress={() => router.replace("/upload")} />
    <Button testID="onboarding-done" variant="secondary" label={t("onboarding.done")} onPress={() => router.replace("/me")} />
  </View>;
}
