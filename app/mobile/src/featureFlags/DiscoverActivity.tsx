import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { useFeatureFlag } from "./FeatureFlagsProvider";

export function DiscoverActivity() {
  const enabled = useFeatureFlag("discover-activity");
  const theme = useTheme();
  const { t } = useTranslation();
  if (!enabled) return null;
  return (
    <View testID="discover-activity" style={{ padding: theme.space[4], backgroundColor: theme.colors.surface }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>
        {t("featureFlags.activityTitle")}
      </Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("featureFlags.activityPlaceholder")}</Text>
    </View>
  );
}
