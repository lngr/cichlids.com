import { useState } from "react";
import { Pressable, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { TankVisibilityDto } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { badgeFor, draftAfterProfileChange, draftAfterStreamChange, isDraftChanged, requiresConsentText, type Levels } from "./tankVisibility";

type Level = Levels["profile"];

/**
 * The tank settings of the owner for who sees the tank: a prominent badge for a tank that is not fully
 * public, and the levels of the tank profile and of the care stream with the way to change them. The
 * care stream is never offered wider than the profile.
 */
export function TankVisibilitySettings({ tankId, visibility, onChanged }: { tankId: number; visibility: TankVisibilityDto; onChanged: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Levels>({ profile: visibility.profile, stream: visibility.stream });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const badge = badgeFor(visibility);
  const changed = isDraftChanged(visibility, draft);
  async function save() {
    setBusy(true); setError(null); setSaved(false);
    try {
      await apiClient.tanks.setVisibility(tankId, { profile: draft.profile, stream: draft.stream });
      setSaved(true);
      onChanged();
    } catch (err) { setError(translateApiError(err, t, "common.loadError")); }
    finally { setBusy(false); }
  }
  return <View testID="tank-visibility" style={{ gap: theme.space[3], padding: theme.space[4], borderWidth: 1, borderRadius: theme.radius.lg, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}>
    {badge ? <View testID="tank-private-badge" accessible accessibilityRole="text" accessibilityLabel={t(badge === "private" ? "tanks.visibility.badgePrivate" : "tanks.visibility.badgeStreamPrivate")}
      style={{ minHeight: 48, justifyContent: "center", paddingHorizontal: theme.space[3], borderWidth: 2, borderRadius: theme.radius.md, borderColor: theme.colors.fg, backgroundColor: theme.colors.accentWeak }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(badge === "private" ? "tanks.visibility.badgePrivate" : "tanks.visibility.badgeStreamPrivate")}</Text>
    </View> : null}
    <View style={{ gap: theme.space[1] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("tanks.visibility.title")}</Text>
      <Text testID="tank-visibility-profile" style={[theme.type.body, { color: theme.colors.fg }]}>{`${t("tanks.visibility.profileTitle")}: ${t(`tanks.visibility.${visibility.profile}`)}`}</Text>
      <Text testID="tank-visibility-stream" style={[theme.type.body, { color: theme.colors.fg }]}>{`${t("tanks.visibility.streamTitle")}: ${t(`tanks.visibility.${visibility.stream}`)}`}</Text>
    </View>
    <Button testID="tank-visibility-open" variant="secondary" label={t(open ? "navigation.cancel" : "tanks.visibility.title")} onPress={() => { setOpen(value => !value); setDraft({ profile: visibility.profile, stream: visibility.stream }); setError(null); setSaved(false); }} />
    {open ? <>
      <LevelGroup testID="tank-visibility-profile-choice" title={t("tanks.visibility.profileTitle")} help={t("tanks.visibility.profileHelp")} value={draft.profile}
        onChange={level => setDraft(current => draftAfterProfileChange(current, level))} />
      <LevelGroup testID="tank-visibility-stream-choice" title={t("tanks.visibility.streamTitle")} help={t("tanks.visibility.streamHelp")} value={draft.stream}
        blockedPublic={draft.profile === "private"} onChange={level => setDraft(current => draftAfterStreamChange(current, level))} />
      {draft.profile === "private" ? <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("tanks.visibility.streamBlocked")}</Text> : null}
      {requiresConsentText(visibility, draft) ? <Text testID="tank-visibility-consent" style={[theme.type.body, { color: theme.colors.fg }]}>{t("tanks.visibility.consent")}</Text> : null}
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
      {saved ? <Text testID="tank-visibility-saved" accessibilityLiveRegion="polite" style={[theme.type.body, { color: theme.colors.fg }]}>{t("tanks.visibility.saved")}</Text> : null}
      <Button testID="tank-visibility-save" label={t("tanks.visibility.save")} busy={busy} disabled={!changed} onPress={() => void save()} />
    </> : null}
  </View>;
}

function LevelGroup({ testID, title, help, value, onChange, blockedPublic = false }: {
  testID: string; title: string; help: string; value: Level; onChange: (level: Level) => void; blockedPublic?: boolean;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  return <View accessibilityRole="radiogroup" accessibilityLabel={title} style={{ gap: theme.space[2] }}>
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{title}</Text>
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{help}</Text>
    {(["public", "private"] as const).map(level => {
      const disabled = blockedPublic && level === "public";
      const label = t(`tanks.visibility.${level}`);
      return <Pressable key={level} testID={`${testID}-${level}`} accessibilityRole="radio" accessibilityLabel={label}
        accessibilityState={{ selected: value === level, disabled }} disabled={disabled} onPress={() => onChange(level)}
        style={{ minHeight: 48, justifyContent: "center", padding: theme.space[3], borderWidth: value === level ? 2 : 1, opacity: disabled ? 0.5 : 1,
          borderColor: value === level ? theme.colors.fg : theme.colors.border, backgroundColor: theme.colors.surface2, borderRadius: theme.radius.md }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{label}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t(`tanks.visibility.${level}Help`)}</Text>
      </Pressable>;
    })}
  </View>;
}
