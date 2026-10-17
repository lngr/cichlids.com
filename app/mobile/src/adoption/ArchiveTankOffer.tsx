import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { adoptionRoute } from "./adoptionView";

/**
 * The offer on the page of an own tank to adopt it while it is an archive tank that is not adopted. The
 * card is absent for every other tank and while the archive tanks load.
 */
export function ArchiveTankOffer({ tankId }: { tankId: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [open, setOpen] = useState(false);
  useEffect(() => {
    let cancelled = false;
    apiClient.me.legacyTanks()
      .then(tanks => { if (!cancelled) setOpen(tanks.some(tank => Number(tank.id) === tankId)); })
      .catch(() => { if (!cancelled) setOpen(false); });
    return () => { cancelled = true; };
  }, [tankId]);
  if (!open) return null;
  return <View testID="archive-tank-offer" style={{ marginHorizontal: theme.space[4], marginTop: theme.space[4], gap: theme.space[2], padding: theme.space[4], borderWidth: 1, borderRadius: theme.radius.lg, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("adoption.offerTitle")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("adoption.offerText")}</Text>
    <Button testID="archive-tank-offer-adopt" label={t("adoption.offerAction")} onPress={() => router.push(adoptionRoute(tankId))} />
  </View>;
}
