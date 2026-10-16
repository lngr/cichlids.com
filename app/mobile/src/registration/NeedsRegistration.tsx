import { ScrollView, Text } from "react-native";
import { useTranslation } from "react-i18next";
import type { RegistrationReason } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useRegistration } from "./RegistrationProvider";

/** The content of a screen that only a registered account can use: the reason and the way to register. */
export function NeedsRegistration({ reason }: { reason: RegistrationReason }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const { requestRegistration } = useRegistration();
  return <ScrollView testID="needs-registration" style={{ backgroundColor: theme.colors.bg }}
    contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: theme.space[6], gap: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("registration.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(`registration.reason.${reason}`)}</Text>
    <Button testID="needs-registration-button" label={t("registration.register")} onPress={() => requestRegistration(reason)} />
  </ScrollView>;
}
