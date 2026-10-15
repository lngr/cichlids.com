import type { TankDetail } from "@cichlids/client-core";
import { View, Text } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { TankRegionRefinement } from "../onboarding/TankRegionRefinement";

export function TankRegionSlot({ tank }: { tank: TankDetail }) {
  const { t } = useTranslation();
  const theme = useTheme();
  return <View testID="tank-region-slot" style={tank.category || tank.region ? { alignSelf: "flex-start", backgroundColor: theme.colors.surface, borderRadius: theme.radius.pill, paddingHorizontal: theme.space[3], paddingVertical: theme.space[1] } : undefined}>
    {tank.region ? <TankRegionRefinement tank={tank} /> : tank.category ? <Text style={[theme.type.meta, { color: theme.colors.fg }]}>{t(`tanks.categories.${tank.category}`)}</Text> : null}
  </View>;
}
