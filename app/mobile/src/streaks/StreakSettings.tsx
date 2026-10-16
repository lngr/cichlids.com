import { useCallback, useState } from "react";
import { Text, View } from "react-native";
import { useFocusEffect } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { reminderRefresh } from "../reminders/refreshSignal";
import { useTheme } from "../theme";
import { VacationSettings } from "./VacationSettings";

/**
 * The streak settings of the member: one switch for showing the streaks, and the vacation mode while
 * they are on. The reminders are rebuilt after a change, since the daily water change reminders of a
 * running streak depend on the switch. The callback tells the screen to read the profile again.
 */
export function StreakSettings({ onChanged }: { onChanged?: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useFocusEffect(useCallback(() => {
    let active = true;
    void apiClient.me.settings().then(result => { if (active) setEnabled(result.streaksEnabled); }).catch(() => { if (active) setEnabled(null); });
    return () => { active = false; };
  }, []));
  if (enabled === null) return null;
  const change = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await apiClient.me.setStreaksEnabled(!enabled);
      setEnabled(result.streaksEnabled);
      reminderRefresh.request();
      onChanged?.();
    } catch (err) {
      setError(translateApiError(err, t, "common.loadError"));
    } finally {
      setBusy(false);
    }
  };
  return <View style={{ gap: theme.space[2] }}>
    <View testID="streak-switch" style={{ gap: theme.space[2], paddingVertical: theme.space[4] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("streaks.switch.title")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("streaks.switch.intro")}</Text>
      <Text testID="streak-switch-state" accessibilityLiveRegion="polite" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>
        {t(enabled ? "streaks.switch.on" : "streaks.switch.off")}</Text>
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      <Button testID="streak-switch-toggle" variant="secondary" busy={busy} label={t(enabled ? "streaks.switch.turnOff" : "streaks.switch.turnOn")}
        onPress={() => void change()} />
    </View>
    {enabled ? <VacationSettings /> : null}
  </View>;
}
