import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useRegistration } from "./RegistrationProvider";

/**
 * The state of the device account on the Me tab. A provisional account says that it lives on this
 * device and offers the registration. A signed-in member whose device account stayed behind sees
 * where the content is and the way to take it along. Everyone else sees nothing.
 */
export function DeviceAccountCard() {
  const { provisional, deviceContentLeftBehind } = useAuth();
  const { requestRegistration, takeAlong } = useRegistration();
  const theme = useTheme();
  const { t } = useTranslation();
  if (!provisional && !deviceContentLeftBehind) return null;
  return <View testID={provisional ? "device-account" : "device-account-left-behind"}
    style={{ gap: theme.space[2], marginHorizontal: theme.space[4], marginBottom: theme.space[3], padding: theme.space[4], borderWidth: 1, borderRadius: theme.radius.lg, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(provisional ? "me.device.title" : "me.device.leftBehindTitle")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(provisional ? "me.device.text" : "me.device.leftBehindText")}</Text>
    {provisional
      ? <Button testID="device-account-register" label={t("registration.register")} onPress={() => requestRegistration("register")} />
      : <Button testID="device-account-take-along" label={t("me.device.takeAlong")} onPress={takeAlong} />}
  </View>;
}
