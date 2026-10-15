import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../src/theme";
export default function TodayScreen() {
  const theme = useTheme();
  const { t } = useTranslation();
  return <View testID="today-placeholder" style={{ flex: 1, backgroundColor: theme.colors.bg, padding: theme.space[4] }}>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("navigation.todayPlaceholder")}</Text>
  </View>;
}
