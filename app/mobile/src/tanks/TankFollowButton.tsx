import { useState } from "react";
import { Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TankFollow, TankVisibility } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { useLoginAction } from "../auth/useLoginAction";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { followControlOf, undoesFollow } from "./tankFollow";

/**
 * The follow control of a tank for members who do not own it: follow, ask to follow, the open request
 * (a press withdraws it) and the approved follow (a press stops following). Visitors without an account
 * of a public tank get a sign-in button.
 */
export function TankFollowButton({ tankId, title, profile, follow, isOwner, onChanged }: {
  tankId: number; title: string; profile: TankVisibility; follow: TankFollow | null | undefined; isOwner: boolean; onChanged: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { status } = useAuth();
  const login = useLoginAction();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const control = followControlOf(follow, profile, isOwner, status === "authenticated");
  if (control === "hidden" || status === "loading") return null;
  async function press() {
    setBusy(true); setError(null);
    try {
      if (undoesFollow(control)) await apiClient.tanks.unfollow(tankId);
      else await apiClient.tanks.requestFollow(tankId);
      onChanged();
    } catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); }
  }
  if (control === "login") {
    return <View style={{ gap: theme.space[2] }}>
      <Button testID="tank-follow-login" variant="secondary" label={t("tanks.follow.loginToFollow")} busy={login.busy} onPress={login.onLogin} />
    </View>;
  }
  const label = t(`tanks.follow.${control}`);
  const hint = t(`tanks.follow.${control}Label`, { title });
  return <View style={{ gap: theme.space[2] }}>
    <Button testID="tank-follow-button" variant={undoesFollow(control) ? "secondary" : "primary"} label={label} accessibilityLabel={hint} busy={busy} onPress={() => void press()} />
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
  </View>;
}
