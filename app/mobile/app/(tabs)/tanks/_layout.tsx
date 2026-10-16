import { MachineStack as Stack } from "../../../src/navigation/MachineStack";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../../src/theme";

export default function TanksStackLayout() {
  const theme = useTheme();
  const { t } = useTranslation();
  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: theme.colors.surface },
        headerTintColor: theme.colors.fg,
        cardStyle: { backgroundColor: theme.colors.bg },
      }}
    >
      <Stack.Screen name="index" dangerouslySingular="index" options={{ title: t("tanks.tabTitle") }} />
      <Stack.Screen name="[id]" options={{ title: "" }} />
      <Stack.Screen name="[id]/camera" options={{ title: t("timeLapseCamera.title") }} />
      <Stack.Screen name="[id]/water-change" options={{ title: t("waterChange.title") }} />
    </Stack>
  );
}
