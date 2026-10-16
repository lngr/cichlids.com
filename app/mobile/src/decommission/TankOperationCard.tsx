import { Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import { activeDecommissioning, type DecommissionImage } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { formatLocalDate } from "../streaks/streakText";
import { activeLines, imageOf, operatingLines, pastLine } from "./decommissionText";
import { useTankOperation } from "./useTankOperation";

const THUMB = 96;

/**
 * The operating time of a tank without its decommissioned days and the decommissionings with their
 * photos of the empty and the filled tank, for whoever sees the care stream of the tank. The owner can
 * decommission the tank or put it back into operation from here. Absent while unread or when the
 * viewer does not see the care stream.
 */
export function TankOperationCard({ tankId, owner }: { tankId: number; owner: boolean }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const operation = useTankOperation(tankId);
  if (!operation) return null;
  const active = activeDecommissioning(operation);
  const past = operation.items.filter(item => !item.active);
  const [time, ...rest] = operatingLines(operation, t);
  const muted = { color: theme.colors.muted };
  const photo = (testID: string, image: DecommissionImage | null, label: string) => {
    const source = imageOf(image);
    return source ? <View key={testID} style={{ gap: theme.space[1], width: THUMB }}>
      <Image testID={testID} source={source} accessibilityLabel={label} contentFit="cover"
        style={{ width: THUMB, height: THUMB, borderRadius: theme.radius.sm, backgroundColor: theme.colors.placeholder }} />
      <Text style={[theme.type.meta, muted]}>{label}</Text>
    </View> : null;
  };
  return <View testID="tank-operation" style={{ gap: theme.space[2], padding: theme.space[4], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, muted]}>{t("decommission.cardTitle")}</Text>
    <Text testID="tank-operation-time" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{time}</Text>
    {rest.map((line, index) => <Text key={index} testID="tank-operation-rest" style={[theme.type.body, muted]}>{line}</Text>)}
    {active ? <View testID="tank-operation-active" style={{ gap: theme.space[2] }}>
      {activeLines(active, t, i18n.language).map((line, index) => <Text key={index} style={[theme.type.body, { color: theme.colors.fg }]}>{line}</Text>)}
      <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[3] }}>
        {photo(`tank-operation-empty-${active.id}`, active.startImage, t("decommission.emptyPhoto", { date: formatLocalDate(active.startDate, i18n.language) }))}
      </View>
    </View> : null}
    {past.length > 0 ? <View testID="tank-operation-history" style={{ gap: theme.space[3] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("decommission.history")}</Text>
      {past.map(item => <View key={item.id} testID={`tank-operation-past-${item.id}`} style={{ gap: theme.space[2] }}>
        <Text style={[theme.type.body, muted]}>{pastLine(item, t, i18n.language)}</Text>
        <Text style={[theme.type.meta, muted]}>{t(item.scope === "all_care" ? "decommission.statusAll" : "decommission.statusWater")}</Text>
        <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[3] }}>
          {photo(`tank-operation-empty-${item.id}`, item.startImage, t("decommission.emptyPhoto", { date: formatLocalDate(item.startDate, i18n.language) }))}
          {photo(`tank-operation-filled-${item.id}`, item.endImage, t("decommission.filledPhoto", { date: formatLocalDate(item.endDate!, i18n.language) }))}
        </View>
      </View>)}
    </View> : null}
    {owner ? <Button testID={active ? "tank-recommission" : "tank-decommission"} variant="secondary"
      label={t(active ? "decommission.end" : "decommission.start")} onPress={() => router.push(`/tanks/${tankId}/decommission`)} /> : null}
  </View>;
}
