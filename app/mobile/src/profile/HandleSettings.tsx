import { useState } from "react";
import { Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { minTouchTarget, useTheme } from "../theme";
import { canSubmitHandle, handleErrorText, nextChangeText } from "./handleText";

/**
 * The public handle of the member in the Me tab: the current handle, the day the next change is
 * possible and an editor. A refused handle shows its reason at the field. The callback tells the
 * screen to read the profile again after a change.
 */
export function HandleSettings({ handle, changeAvailableAt, onChanged }: {
  handle: string;
  changeAvailableAt?: string | null;
  onChanged: () => void;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [editing, setEditing] = useState(false);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);
  const next = nextChangeText(changeAvailableAt, new Date(), t, i18n.language);

  const open = () => {
    setInput(handle);
    setError(null);
    setSaved(null);
    setEditing(true);
  };
  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await apiClient.me.setHandle(input.trim());
      setSaved(result.handle);
      setEditing(false);
      onChanged();
    } catch (err) {
      setError(handleErrorText(err, t, i18n.language));
    } finally {
      setBusy(false);
    }
  };

  return <View testID="handle-settings" style={{ gap: theme.space[2], paddingVertical: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("me.handle.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("me.handle.intro")}</Text>
    <Text testID="handle-current" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{`@${handle}`}</Text>
    {saved ? <Text testID="handle-saved" accessibilityLiveRegion="polite" style={[theme.type.body, { color: theme.colors.fg }]}>
      {t("me.handle.saved", { handle: saved })}</Text> : null}
    {next ? <Text testID="handle-next-change" style={[theme.type.meta, { color: theme.colors.muted }]}>{next}</Text> : null}
    {editing ? <View style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("me.handle.label")}</Text>
      <TextInput testID="handle-input" accessibilityLabel={t("me.handle.label")} value={input} editable={!busy} autoCapitalize="none" autoCorrect={false}
        placeholderTextColor={theme.colors.muted} onChangeText={(text) => { setInput(text); setError(null); }}
        style={[theme.type.body, {
          color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
          borderColor: error ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3],
        }]} />
      {error
        ? <Text testID="handle-error" accessibilityRole="alert" style={[theme.type.meta, { color: theme.colors.danger }]}>{error}</Text>
        : <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("me.handle.hint")}</Text>}
      <Button testID="handle-save" label={t("me.handle.save")} busy={busy} disabled={!canSubmitHandle(input, handle)} onPress={() => void save()} />
      <Button testID="handle-cancel" variant="secondary" label={t("me.handle.cancel")} disabled={busy} onPress={() => setEditing(false)} />
    </View> : <Button testID="handle-edit" variant="secondary" label={t("me.handle.change")} onPress={open} />}
  </View>;
}
