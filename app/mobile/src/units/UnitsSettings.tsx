import { useState } from "react";
import { Pressable, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { getUnitPreference, setUnitPreference } from "./preferences";
import { useUnits } from "./useUnits";
import type { UnitSystem } from "./format";

export function UnitsSettings() {
  const theme = useTheme();
  const { t } = useTranslation();
  const system = useUnits();
  const [expanded, setExpanded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const preference = getUnitPreference();
  async function select(value: UnitSystem | null) {
    setBusy(true);
    setError(false);
    try {
      await setUnitPreference(value);
      setExpanded(false);
    } catch { setError(true); }
    finally { setBusy(false); }
  }
  return <View style={{ padding: theme.space[4], gap: theme.space[2], alignSelf: "stretch" }}>
    <Pressable testID="units-settings-toggle" accessibilityRole="button" accessibilityState={{ expanded }} onPress={() => setExpanded(!expanded)} style={{ minHeight: 48, justifyContent: "center" }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("units.title")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{preference ? t(`units.${system}`) : `${t("units.device")} (${t(`units.${system}`)})`}</Text>
    </Pressable>
    {expanded ? <View accessibilityRole="radiogroup" accessibilityLabel={t("units.title")} style={{ gap: theme.space[1] }}>
      {([null, "metric", "us"] as const).map((value) => <Pressable key={value ?? "device"} testID={`units-${value ?? "device"}`} accessibilityRole="radio" accessibilityState={{ checked: preference === value, disabled: busy }} disabled={busy} onPress={() => void select(value)} style={{ minHeight: 48, padding: theme.space[3], borderRadius: theme.radius.sm, backgroundColor: preference === value ? theme.colors.accentWeak : theme.colors.surface }}>
        <Text style={[theme.type.body, { color: theme.colors.fg }]}>{value ? t(`units.${value}`) : t("units.device")}</Text>
      </Pressable>)}
    </View> : null}
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("settings.saveError")}</Text> : null}
  </View>;
}
