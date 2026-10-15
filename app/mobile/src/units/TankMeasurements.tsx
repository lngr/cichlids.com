import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { TankDetail } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { DataRow } from "../components/DataRow";
import { formatMeasurement, type MeasurementInput, type UnitSystem } from "./format";
import { useUnits } from "./useUnits";

export function TankMeasurements({ tank }: { tank: TankDetail }) {
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
  return <>
    <DataRow label={t("tanks.detail.dimensions")} value={dimensionText} />
    <DataRow label={t("tanks.detail.volume")} value={volume ? formatMeasurement(Number(volume.liters), "volume", preference, i18n.language, volumeInput, owner) : null} />
  </>;
}
