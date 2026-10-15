import { ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../src/theme";
import { Button } from "../../src/components/Button";
import { DiscoverActivity } from "../../src/featureFlags/DiscoverActivity";
import { useMachineRouter } from "../../src/navigation/NavigationProvider";
import { DiscoverArchive } from "../../src/discover/DiscoverArchive";
export default function DiscoverScreen() {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  return <ScrollView style={{ flex: 1, backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[4] }}>
    <DiscoverArchive />
    <View testID="discover-archive" style={{ gap: theme.space[3] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("navigation.archive")}</Text>
      <Button testID="discover-gallery" variant="secondary" label={t("tabs.gallery")} onPress={() => router.push("/gallery")} />
      <Button testID="discover-tanks" variant="secondary" label={t("tabs.tanks")} onPress={() => router.push("/tanks")} />
      <Button testID="discover-community" variant="secondary" label={t("community.tabTitle")} onPress={() => router.push("/community")} />
    </View>
    <DiscoverActivity />
  </ScrollView>;
}
