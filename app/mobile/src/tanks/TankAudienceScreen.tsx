import { useCallback, useEffect, useState } from "react";
import { ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TankMember } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { formatDate } from "../lib/format";
import { translateApiError } from "../lib/apiErrors";
import { minTouchTarget, useTheme } from "../theme";

type Audience = { requests: TankMember[]; followers: TankMember[]; selected: TankMember[] };

const PAGE = 100;

/**
 * The screen of the owner for the audience of a tank: open follow requests to approve or decline, the
 * approved followers to remove, and the selected members to add by handle and to remove.
 */
export function TankAudienceScreen({ tankId }: { tankId: number }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [audience, setAudience] = useState<Audience | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [handle, setHandle] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const load = useCallback(async () => {
    try {
      const [requests, followers, selected] = await Promise.all([
        apiClient.tanks.followRequests(tankId, { offset: 0, limit: PAGE }),
        apiClient.tanks.followers(tankId, { offset: 0, limit: PAGE }),
        apiClient.tanks.selectedMembers(tankId),
      ]);
      setAudience({ requests: requests.items, followers: followers.items, selected });
      setLoadError(null);
    } catch (err) { setLoadError(translateApiError(err, t, "common.loadError")); }
  }, [tankId, t]);
  useEffect(() => { void load(); }, [load]);
  async function run(key: string, action: () => Promise<unknown>) {
    setBusy(key); setActionError(null);
    try { await action(); await load(); }
    catch (err) { setActionError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(null); }
  }
  if (loadError && !audience) return <ErrorView message={loadError} onRetry={() => void load()} />;
  if (!audience) return <LoadingView label={t("tanks.audience.loading")} />;
  const name = (member: TankMember) => member.member.displayName ?? member.member.username;
  const text = { color: theme.colors.fg };
  const card = { gap: theme.space[3], padding: theme.space[4], borderWidth: 1, borderRadius: theme.radius.lg, borderColor: theme.colors.border, backgroundColor: theme.colors.surface };
  function row(testID: string, member: TankMember, actions: { key: string; testID: string; label: string; a11y: string; variant: "primary" | "secondary"; run: () => Promise<unknown> }[]) {
    return <View key={testID} testID={testID} style={{ gap: theme.space[2], paddingTop: theme.space[2], borderTopWidth: 1, borderColor: theme.colors.border }}>
      <Text style={[theme.type.bodyStrong, text]}>{name(member)}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{`@${member.member.username} · ${t("tanks.audience.since", { date: formatDate(member.since, i18n.language) })}`}</Text>
      <View style={{ flexDirection: "row", gap: theme.space[2] }}>
        {actions.map(action => <View key={action.key} style={{ flex: 1, minHeight: minTouchTarget }}>
          <Button testID={action.testID} variant={action.variant} label={action.label} accessibilityLabel={action.a11y}
            busy={busy === `${testID}-${action.key}`} disabled={busy !== null} onPress={() => void run(`${testID}-${action.key}`, action.run)} />
        </View>)}
      </View>
    </View>;
  }
  return <ScrollView testID="tank-audience" style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h1, text]}>{t("tanks.audience.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("tanks.audience.intro")}</Text>
    {actionError ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{actionError}</Text> : null}

    <View style={card}>
      <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("tanks.audience.requestsTitle", { number: audience.requests.length })}</Text>
      {audience.requests.length === 0 ? <Text testID="audience-requests-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("tanks.audience.requestsEmpty")}</Text> : null}
      {audience.requests.map(member => row(`audience-request-${member.member.id}`, member, [
        { key: "approve", testID: `audience-approve-${member.member.id}`, label: t("tanks.audience.approve"), a11y: t("tanks.audience.approveLabel", { name: name(member) }), variant: "primary", run: () => apiClient.tanks.approveFollowRequest(tankId, Number(member.member.id)) },
        { key: "decline", testID: `audience-decline-${member.member.id}`, label: t("tanks.audience.decline"), a11y: t("tanks.audience.declineLabel", { name: name(member) }), variant: "secondary", run: () => apiClient.tanks.declineFollowRequest(tankId, Number(member.member.id)) },
      ]))}
    </View>

    <View style={card}>
      <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("tanks.audience.followersTitle", { number: audience.followers.length })}</Text>
      {audience.followers.length === 0 ? <Text testID="audience-followers-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("tanks.audience.followersEmpty")}</Text> : null}
      {audience.followers.map(member => row(`audience-follower-${member.member.id}`, member, [
        { key: "remove", testID: `audience-remove-follower-${member.member.id}`, label: t("tanks.audience.remove"), a11y: t("tanks.audience.removeFollowerLabel", { name: name(member) }), variant: "secondary", run: () => apiClient.tanks.removeFollower(tankId, Number(member.member.id)) },
      ]))}
    </View>

    <View style={card}>
      <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("tanks.audience.selectedTitle", { number: audience.selected.length })}</Text>
      {audience.selected.length === 0 ? <Text testID="audience-selected-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("tanks.audience.selectedEmpty")}</Text> : null}
      {audience.selected.map(member => row(`audience-selected-${member.member.id}`, member, [
        { key: "remove", testID: `audience-remove-selected-${member.member.id}`, label: t("tanks.audience.remove"), a11y: t("tanks.audience.removeSelectedLabel", { name: name(member) }), variant: "secondary", run: () => apiClient.tanks.removeSelectedMember(tankId, Number(member.member.id)) },
      ]))}
      <Text accessibilityRole="header" style={[theme.type.bodyStrong, text]}>{t("tanks.audience.addTitle")}</Text>
      <TextInput testID="audience-handle" value={handle} onChangeText={setHandle} autoCapitalize="none" autoCorrect={false}
        accessibilityLabel={t("tanks.audience.handleLabel")} accessibilityHint={t("tanks.audience.handleHint")}
        placeholder={t("tanks.audience.handleLabel")} placeholderTextColor={theme.colors.muted}
        style={[theme.type.body, { minHeight: minTouchTarget, paddingHorizontal: theme.space[3], borderWidth: 1, borderRadius: theme.radius.md, borderColor: theme.colors.border, color: theme.colors.fg, backgroundColor: theme.colors.surface2 }]} />
      <Button testID="audience-add" label={t("tanks.audience.add")} busy={busy === "add"} disabled={handle.trim() === "" || busy !== null}
        onPress={() => void run("add", async () => { await apiClient.tanks.addSelectedMember(tankId, handle.trim()); setHandle(""); })} />
    </View>
  </ScrollView>;
}
