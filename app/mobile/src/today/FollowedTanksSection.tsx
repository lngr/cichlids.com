import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { FollowedTankCard } from "./FollowedTankCard";
import { useFollowedTanks } from "./useFollowedTanks";

/**
 * The followed tanks with a fresh update, newest first, with a link to all followed tanks. Absent
 * while no followed tank has an update the member may see.
 */
export function FollowedTanksSection() {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const followed = useFollowedTanks();
  if (!followed || followed.items.length === 0) return null;
  return <View testID="today-followed" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("today.followed.title")}</Text>
    {followed.items.map(tank => <FollowedTankCard key={tank.tankId} tank={tank} testID={`today-followed-${tank.tankId}`} />)}
    <Button testID="today-followed-all" variant="secondary" label={t("today.followed.all")} onPress={() => router.push("/followed")} />
  </View>;
}
