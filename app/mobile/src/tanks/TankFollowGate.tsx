import { useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TankFollow } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useRegistrationGate } from "../registration/RegistrationProvider";
import { useTheme } from "../theme";

/**
 * The page of a tank for followers that the member does not see yet: the owner and the way to ask for the
 * follow, or the open request with the way to withdraw it and to check for the answer.
 */
export function TankFollowGate({ tankId, follow, onChanged }: { tankId: number; follow: TankFollow; onChanged: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const gate = useRegistrationGate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const owner = follow.teaser?.author.displayName ?? follow.teaser?.author.username ?? "";
  const requested = follow.state === "requested";
  async function press() {
    if (gate("follow")) return;
    setBusy(true); setError(null);
    try {
      if (requested) await apiClient.tanks.unfollow(tankId); else await apiClient.tanks.requestFollow(tankId);
      onChanged();
    } catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); }
  }
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3] }}>
    <View testID="tank-follow-gate" style={{ gap: theme.space[3], padding: theme.space[4], borderWidth: 1, borderRadius: theme.radius.lg, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}>
      <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("tanks.follow.lockedTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("tanks.follow.lockedHelp", { owner })}</Text>
      {requested ? <Text testID="tank-follow-gate-requested" accessibilityLiveRegion="polite" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("tanks.follow.requestedHelp", { owner })}</Text> : null}
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      <Button testID="tank-follow-gate-button" variant={requested ? "secondary" : "primary"} busy={busy}
        label={t(requested ? "tanks.follow.requested" : "tanks.follow.request")} onPress={() => void press()}
        accessibilityLabel={t(requested ? "tanks.follow.requestedLabel" : "tanks.follow.requestLabel", { title: owner })} />
      {requested ? <Button testID="tank-follow-gate-refresh" variant="secondary" label={t("common.retry")} onPress={onChanged} /> : null}
    </View>
  </ScrollView>;
}
