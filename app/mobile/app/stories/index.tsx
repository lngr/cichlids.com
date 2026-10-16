import { ScrollView, Text } from "react-native";
import { useTranslation } from "react-i18next";
import { useAuth } from "../../src/auth/AuthProvider";
import { useLoginAction } from "../../src/auth/useLoginAction";
import { Button } from "../../src/components/Button";
import { LoadingView } from "../../src/components/StatusView";
import { StoriesView } from "../../src/stories/StoriesView";
import { useTheme } from "../../src/theme";

export default function StoriesScreen() {
  const { status, accountId } = useAuth();
  if (status === "loading") return <LoadingView />;
  return status === "authenticated" ? <StoriesView key={accountId} /> : <SignIn />;
}

function SignIn() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { busy, failure, onLogin } = useLoginAction();
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: theme.space[6], gap: theme.space[4] }}>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("stories.signIn")}</Text>
    {failure ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t(failure === "unavailable" ? "me.loginUnavailable" : "me.loginError")}</Text> : null}
    <Button testID="stories-login" label={t(failure ? "common.retry" : "me.login")} onPress={onLogin} busy={busy} />
  </ScrollView>;
}
