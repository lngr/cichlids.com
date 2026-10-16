import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { activeVacation } from "@cichlids/client-core";
import { localCalendarDate } from "../today/localDate";
import { useTheme } from "../theme";
import { StreakCards } from "./StreakCards";
import { formatLocalDate } from "./streakText";
import { useOwnStreaks } from "./useStreaks";

/**
 * The streaks of the member on Today: per own tank the water change streak with its next deadline and
 * the time-lapse streak, and a line while a vacation is active. Absent without tanks or while the
 * streaks cannot be read.
 */
export function TodayStreaks() {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const streaks = useOwnStreaks();
  if (!streaks || streaks.tanks.length === 0) return null;
  const vacation = activeVacation(streaks.vacations);
  const today = localCalendarDate();
  return <View testID="today-streaks" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("streaks.yourTitle")}</Text>
    {vacation ? <Text testID="today-streaks-vacation" style={[theme.type.body, { color: theme.colors.muted }]}>
      {t("streaks.vacation.active", { date: formatLocalDate(vacation.startDate, i18n.language) })}</Text> : null}
    {streaks.tanks.map(tank => <View key={tank.tankId} testID={`today-streak-${tank.tankId}`} style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{tank.title || t("common.untitled")}</Text>
      <StreakCards prefix={`today-streak-${tank.tankId}`} waterChange={tank.waterChange} timeLapse={tank.timeLapse} today={today} />
    </View>)}
  </View>;
}
