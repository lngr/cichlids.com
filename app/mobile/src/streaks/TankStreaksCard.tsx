import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { localCalendarDate } from "../today/localDate";
import { useTheme } from "../theme";
import { StreakCards } from "./StreakCards";
import { pastVacationLines } from "./streakText";
import { useTankStreaks } from "./useStreaks";

const PAST_VACATIONS_SHOWN = 3;

/**
 * The streaks of a tank for whoever sees them: the owner with deadline and freezes, others with the
 * counts they may see, and the ended vacations of the owner. Absent while the streaks are unread or
 * when the viewer sees neither streak.
 */
export function TankStreaksCard({ tankId }: { tankId: number }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const streaks = useTankStreaks(tankId);
  if (!streaks || (streaks.waterChange === null && streaks.timeLapse === null)) return null;
  const vacations = pastVacationLines(streaks.vacations, t, i18n.language, PAST_VACATIONS_SHOWN);
  return <View testID="tank-streaks" style={{ gap: theme.space[2] }}>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{t("streaks.title")}</Text>
    <StreakCards prefix="tank-streak" waterChange={streaks.waterChange} timeLapse={streaks.timeLapse} today={localCalendarDate()} />
    {vacations.length > 0 ? <View testID="tank-streaks-vacations" style={{ gap: theme.space[1] }}>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("streaks.vacation.past")}</Text>
      {vacations.map((line, index) => <Text key={index} style={[theme.type.meta, { color: theme.colors.muted }]}>{line}</Text>)}
    </View> : null}
  </View>;
}
