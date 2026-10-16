import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { REMINDER_HOUR, REMINDER_MINUTE } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { remindersSupported } from "./platform";
import { useReminderPermission } from "./useReminderPermission";

/**
 * The reminder switch of a tank in the care view. Where the device schedules reminders, it also
 * explains them before the system asks for the notification permission and points to the system
 * settings when the permission is denied.
 */
export function ReminderSettings({ enabled, busy, onToggle }: { enabled: boolean; busy: boolean; onToggle: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const { prompt, allow, postpone, openSettings } = useReminderPermission(enabled);
  const time = new Intl.DateTimeFormat(i18n.language, { timeStyle: "short" }).format(new Date(2000, 0, 1, REMINDER_HOUR, REMINDER_MINUTE));
  const stateKey = !enabled ? "care.reminders.stateOff" : remindersSupported ? "care.reminders.stateOn" : "care.reminders.stateOnElsewhere";
  const text = { color: theme.colors.fg };
  return <View testID="care-reminders" style={{ gap: theme.space[2], padding: theme.space[3], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, text]}>{t("care.reminders.title")}</Text>
    <Text testID="care-reminders-state" accessibilityLiveRegion="polite" style={[theme.type.body, text]}>{t(stateKey, { time })}</Text>
    <Button testID="care-reminders-toggle" variant="secondary" busy={busy} label={t(enabled ? "care.reminders.switchOff" : "care.reminders.switchOn")} onPress={onToggle} />
    {prompt === "explain" ? <View testID="care-reminders-explain" style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.body, text]}>{t("care.reminders.explain", { time })}</Text>
      <Button testID="care-reminders-allow" label={t("care.reminders.allow")} onPress={() => void allow()} />
      <Button testID="care-reminders-postpone" variant="secondary" label={t("care.reminders.notNow")} onPress={postpone} />
    </View> : null}
    {prompt === "denied" ? <View testID="care-reminders-denied" style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("care.reminders.denied")}</Text>
      <Button testID="care-reminders-settings" variant="secondary" label={t("care.reminders.openSettings")} onPress={openSettings} />
    </View> : null}
  </View>;
}
