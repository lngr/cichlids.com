import { Platform, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { REMINDER_HOUR, REMINDER_MINUTE } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useWebPush } from "./useWebPush";

/**
 * The reminder setting of this browser: off until the member switches it on and agrees to the
 * explanation of what the push service of the browser maker learns. A browser that cannot receive
 * Web Push shows a calm hint instead. The native apps schedule local notifications and show nothing here.
 */
export function WebPushSettings() {
  return Platform.OS === "web" ? <BrowserSettings /> : null;
}

function BrowserSettings() {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const { state, busy, error, switchOn, cancel, agree, switchOff } = useWebPush();
  if (state === "loading" || state === "unavailable") return null;
  const time = new Intl.DateTimeFormat(i18n.language, { timeStyle: "short" }).format(new Date(2000, 0, 1, REMINDER_HOUR, REMINDER_MINUTE));
  const text = { color: theme.colors.fg };
  const hint = state === "unsupported" ? "webPush.unsupported" : state === "needs_home_screen" ? "webPush.needsHomeScreen" : state === "denied" ? "webPush.denied" : null;
  return <View testID="web-push" style={{ gap: theme.space[2], padding: theme.space[3], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, text]}>{t("webPush.title")}</Text>
    {hint ? <Text testID="web-push-hint" accessibilityLiveRegion="polite" style={[theme.type.body, { color: theme.colors.muted }]}>{t(hint)}</Text> : null}
    {state === "off" || state === "explain" || state === "on" ? <Text testID="web-push-state" accessibilityLiveRegion="polite" style={[theme.type.body, text]}>
      {t(state === "on" ? "webPush.stateOn" : "webPush.stateOff", { time })}
    </Text> : null}
    {state === "off" ? <Button testID="web-push-toggle" variant="secondary" busy={busy} label={t("webPush.switchOn")} onPress={switchOn} /> : null}
    {state === "on" ? <Button testID="web-push-toggle" variant="secondary" busy={busy} label={t("webPush.switchOff")} onPress={() => void switchOff()} /> : null}
    {state === "explain" ? <View testID="web-push-explain" style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.body, text]}>{t("webPush.explain")}</Text>
      <Button testID="web-push-agree" label={t("webPush.agree")} onPress={() => void agree()} />
      <Button testID="web-push-cancel" variant="secondary" label={t("webPush.notNow")} onPress={cancel} />
    </View> : null}
    {error ? <Text testID="web-push-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
  </View>;
}
