import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TankDetail, TankRegion } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";

export function TankRegionRefinement({ tank, onRefined }: { tank: TankDetail; onRefined?: (region: TankRegion) => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const { status, accountId } = useAuth();
  const [owner, setOwner] = useState(false);
  const [region, setRegion] = useState(tank.region);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRegion(tank.region); }, [tank.id, tank.region]);
  useEffect(() => {
    let cancelled = false;
    setOwner(false);
    if (status === "authenticated" && tank.region === "americas_unassigned") {
      void apiClient.me.get().then(me => { if (!cancelled) setOwner(String(me.profile.id) === String(tank.author.id)); }).catch(() => {});
    }
    return () => { cancelled = true; };
  }, [status, accountId, tank.author.id, tank.region]);
  async function refine(value: "central_america" | "south_america") {
    if (busy) return;
    setBusy(true); setError(null);
    try { await apiClient.tanks.refineRegion(Number(tank.id), value); setRegion(value); onRefined?.(value); }
    catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); }
  }
  if (!region) return null;
  return <View testID="tank-region" style={{ gap: theme.space[2] }}>
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`regions.${region}`)}</Text>
    {owner && region === "americas_unassigned" ? <>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("onboarding.refine")}</Text>
      {(["central_america", "south_america"] as const).map(value => <Button key={value} testID={`refine-${value}`} variant="secondary" label={t(`regions.${value}`)} busy={busy} onPress={() => void refine(value)} />)}
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    </> : null}
  </View>;
}
