import { Text, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { milestoneText } from "./todaySectionsText";
import { useMilestones } from "./useMilestones";

/**
 * The congratulations on milestones of the own tanks, each with a button that dismisses it for good.
 * Absent while no milestone is open.
 */
export function MilestonesSection() {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const { milestones, dismiss } = useMilestones();
  if (milestones.length === 0) return null;
  return <View testID="today-milestones" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("today.milestones.title")}</Text>
    {milestones.map(milestone => <View key={milestone.key} testID={`today-milestone-${milestone.key}`}
      style={{ gap: theme.space[2], padding: theme.space[4], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>
      <View style={{ flexDirection: "row", alignItems: "center", gap: theme.space[3] }}>
        <MaterialCommunityIcons name="party-popper" size={24} color={theme.colors.accent} />
        <Text style={[theme.type.body, { flex: 1, color: theme.colors.fg }]}>{milestoneText(milestone, t)}</Text>
      </View>
      {milestone.inhabitantId !== null ? <Button testID={`today-milestone-open-${milestone.key}`} variant="secondary"
        label={t("today.milestones.openFish", { name: milestone.inhabitantName ?? t("common.untitled") })}
        onPress={() => router.push(`/fish/${milestone.inhabitantId}`)} /> : null}
      <Button testID={`today-milestone-dismiss-${milestone.key}`} variant="secondary" label={t("today.milestones.dismiss")}
        accessibilityLabel={t("today.milestones.dismissFor", { tank: milestone.inhabitantName ?? (milestone.tankTitle || t("common.untitled")) })}
        onPress={() => dismiss(milestone.key)} />
    </View>)}
  </View>;
}
