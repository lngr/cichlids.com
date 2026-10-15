import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { ProfileDetail } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { RegionChoices } from "./RegionChoices";
import { interestRegions, type SelectableRegion } from "./standardTanks";

export function OnboardingProfile({ profile }: { profile: ProfileDetail }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [editing, setEditing] = useState(false);
  const [interests, setInterests] = useState(profile.interests ?? []);
  const [busy, setBusy] = useState(false);
  useEffect(() => { setInterests(profile.interests ?? []); }, [profile.interests]);
  const [error, setError] = useState<string | null>(null);
  async function save() {
    setBusy(true); setError(null);
    try { setInterests(await apiClient.me.setInterests(interests)); setEditing(false); }
    catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); }
  }
  return <View style={{ padding: theme.space[4], gap: theme.space[3] }}>
    {(profile.badges ?? []).map(badge => <Text key={badge} testID={`badge-${badge}`} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`badges.${badge}`)}</Text>)}
    <Button testID="onboarding-open" variant="secondary" label={t("onboarding.title")} onPress={() => router.push("/onboarding")} />
    <Button testID="interests-edit" variant="secondary" label={t("onboarding.interests")} onPress={() => setEditing(value => !value)} />
    {editing ? <>
      <RegionChoices multiple regions={interestRegions} selected={interests} onChange={(region: SelectableRegion) => setInterests(values => values.includes(region) ? values.filter(v => v !== region) : [...values, region])} />
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      <Button testID="interests-save" label={t("onboarding.save")} busy={busy} onPress={() => void save()} />
    </> : <Text style={[theme.type.body, { color: theme.colors.muted }]}>{interests.map(region => t(`regions.${region}`)).join(", ")}</Text>}
  </View>;
}
