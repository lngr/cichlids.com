import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TimeLapseStreak, WaterChangeStreak } from "@cichlids/client-core";
import { useTheme } from "../theme";
import {
  freezesText, longestText, timeLapseHeadline, timeLapseStatus, verifiedText, waterChangeHeadline, waterChangeStatus,
} from "./streakText";

/**
 * The two streaks of a tank side by side as short blocks: the water change streak (null when the
 * viewer does not see the care stream) and the time-lapse streak (null without access to the tank
 * profile). Each block reads as one item for a screen reader. today is the local calendar date.
 */
export function StreakCards({ prefix, waterChange, timeLapse, today }: {
  prefix: string;
  waterChange: WaterChangeStreak | null;
  timeLapse: TimeLapseStreak | null;
  today: string;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const block = (id: string, title: string, headline: string, lines: (string | null)[]) => {
    const shown = lines.filter((line): line is string => line !== null);
    return <View key={id} testID={`${prefix}-${id}`} accessible accessibilityLabel={[title, headline, ...shown].join(". ")}
      style={{ gap: theme.space[1], padding: theme.space[4], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>
      <Text style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{title}</Text>
      <Text testID={`${prefix}-${id}-headline`} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{headline}</Text>
      {shown.map((line, index) => <Text key={index} style={[theme.type.body, { color: theme.colors.muted }]}>{line}</Text>)}
    </View>;
  };
  return <View style={{ gap: theme.space[2] }}>
    {waterChange ? block("water-change", t("streaks.waterChange.title"), waterChangeHeadline(waterChange, t), [
      verifiedText(waterChange, t), waterChangeStatus(waterChange, today, t, i18n.language), longestText(waterChange.longest, waterChange.current, t),
      freezesText(waterChange, t),
    ]) : null}
    {timeLapse ? block("time-lapse", t("streaks.timeLapse.title"), timeLapseHeadline(timeLapse, t), [
      timeLapseStatus(timeLapse, t, i18n.language), longestText(timeLapse.longest, timeLapse.current, t),
    ]) : null}
  </View>;
}
