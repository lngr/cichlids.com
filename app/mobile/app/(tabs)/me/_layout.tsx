import { MachineStack as Stack } from "../../../src/navigation/MachineStack";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../../src/theme";

export default function MeStackLayout() {
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
      <Stack.Screen name="index" dangerouslySingular="index" options={{ title: t("me.title") }} />
    </Stack>
  );
}
