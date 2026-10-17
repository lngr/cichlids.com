import { Pressable, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import { updateImageUri, updatePath, type FollowedTank } from "@cichlids/client-core";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { minTouchTarget, useTheme } from "../theme";
import { followedLabel, followedLine } from "./todaySectionsText";

/** A followed tank with its newest update; a tap opens the story of the update or the tank. */
export function FollowedTankCard({ tank, testID }: { tank: FollowedTank; testID: string }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const now = new Date();
  const uri = tank.update ? updateImageUri(tank.update) : null;
  const owner = tank.owner.displayName || tank.owner.username;
  return <Pressable testID={testID} accessibilityRole="link" accessibilityLabel={followedLabel(tank, now, t)} onPress={() => router.push(updatePath(tank))}
    style={({ pressed }) => ({ minHeight: minTouchTarget, borderRadius: theme.radius.lg, overflow: "hidden", backgroundColor: theme.colors.surface, opacity: pressed ? 0.85 : 1 })}>
    {uri ? <Image source={uri} accessibilityLabel={tank.title} contentFit="cover" style={{ width: "100%", aspectRatio: 16 / 9, backgroundColor: theme.colors.placeholder }} /> : null}
    <View style={{ minHeight: minTouchTarget, padding: theme.space[4], gap: theme.space[1] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{tank.title || t("common.untitled")}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("today.followed.owner", { name: owner })}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{followedLine(tank, now, t)}</Text>
    </View>
  </Pressable>;
}
