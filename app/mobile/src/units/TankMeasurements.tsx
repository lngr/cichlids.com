import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { useTheme } from "../theme";
import { useTranslation } from "react-i18next";
import type { TankDetail } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { DataRow } from "../components/DataRow";
import { formatMeasurement, type MeasurementInput, type UnitSystem } from "./format";
import { useUnits } from "./useUnits";

export function TankMeasurements({ tank, layout = "rows" }: { tank: TankDetail; layout?: "rows" | "summary" }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const preference = useUnits();
  const { status, accountId } = useAuth();
  const [member, setMember] = useState<{ accountId: string; profileId: string } | null>(null);
  useEffect(() => {
    let cancelled = false;
    if (status === "authenticated" && accountId) {
      apiClient.me.get().then((me) => {
        if (!cancelled) setMember({ accountId, profileId: String(me.profile.id) });
      }).catch(() => { if (!cancelled) setMember(null); });
    }
    return () => { cancelled = true; };
  }, [status, accountId]);
  const owner = status === "authenticated" && accountId === member?.accountId && String(tank.author.id) === member?.profileId;
  const dimensions = tank.dimensions;
  const inputSystem: UnitSystem = dimensions?.inputUnit === "inch" ? "us" : "metric";
  function length(metric: number | string | null | undefined, inputValue: number | string | null | undefined): string {
    if (metric == null) return t("common.unknownValue");
    const input: MeasurementInput | undefined = inputValue != null && dimensions?.inputUnit
      ? { value: Number(inputValue), system: inputSystem } : undefined;
    return formatMeasurement(Number(metric), "length", preference, i18n.language, input, owner);
  }
  const dimensionText = dimensions ? t("tanks.detail.dimensionValue", {
    width: length(dimensions.width, dimensions.inputWidth),
    height: length(dimensions.height, dimensions.inputHeight),
    depth: length(dimensions.depth, dimensions.inputDepth),
    unit: "",
  }).trim() : null;
  const volume = tank.volume;
  const volumeInput: MeasurementInput | undefined = volume?.inputValue != null && volume.inputUnit
    ? { value: Number(volume.inputValue), system: volume.inputUnit === "us_gallon" ? "us" : "metric" } : undefined;
  const volumeText = volume ? formatMeasurement(Number(volume.liters), "volume", preference, i18n.language, volumeInput, owner) : null;
  if (layout === "summary") return <>
    {[[volumeText, t("tanks.detail.volume")], [dimensionText, t("tanks.detail.dimensions")]].map(([value, label]) => <View key={label} style={{ flex: 1, minWidth: 0, padding: theme.space[2], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface, alignItems: "center", justifyContent: "center", gap: theme.space[1] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, textAlign: "center" }]}>{value ?? t("common.unknownValue")}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted, textAlign: "center" }]}>{label}</Text>
    </View>)}
  </>;
  return <>
    <DataRow label={t("tanks.detail.dimensions")} value={dimensionText} />
    <DataRow label={t("tanks.detail.volume")} value={volumeText} />
  </>;
}
