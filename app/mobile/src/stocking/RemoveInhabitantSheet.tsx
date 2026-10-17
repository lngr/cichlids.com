import { useState } from "react";
import { Modal, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { checkRemovalDay, REMOVAL_REASONS, type RemovalReason, type StockedInhabitant } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { localCalendarDate, localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { removalProblemKey } from "./stockingText";

/**
 * A sheet to remove a current inhabitant from the stocking: the reason (died, given away, moved to
 * another tank) and the day it left, which defaults to the local date of the member.
 */
export function RemoveInhabitantSheet({ tankId, inhabitant, label, onRemoved, onCancel }: {
  tankId: number;
  inhabitant: StockedInhabitant | null;
  label: string;
  onRemoved: () => void;
  onCancel: () => void;
}) {
  const theme = useTheme();
  return <Modal visible={inhabitant !== null} transparent animationType="slide" onRequestClose={onCancel}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
      <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[3] }} keyboardShouldPersistTaps="handled">
        {inhabitant ? <Sheet key={inhabitant.id} tankId={tankId} inhabitant={inhabitant} label={label} onRemoved={onRemoved} onCancel={onCancel} /> : null}
      </ScrollView>
    </View>
  </Modal>;
}

function Sheet({ tankId, inhabitant, label, onRemoved, onCancel }: {
  tankId: number; inhabitant: StockedInhabitant; label: string; onRemoved: () => void; onCancel: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [reason, setReason] = useState<RemovalReason | null>(null);
  const [day, setDay] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const problem = removalProblemKey(checkRemovalDay(day, inhabitant.movedInOn, localCalendarDate()));
  async function remove() {
    if (reason === null || problem !== null) return;
    setBusy(true);
    setError(null);
    try {
      const removedOn = day.trim();
      await apiClient.tanks.removeInhabitant(tankId, inhabitant.id, removedOn === "" ? { reason } : { reason, removedOn }, localTime());
      onRemoved();
    } catch (err) {
      setError(translateApiError(err, t, "stocking.saveError"));
    } finally {
      setBusy(false);
    }
  }
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("stocking.removeTitle", { name: label })}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("stocking.removeHint")}</Text>
    <View accessibilityRole="radiogroup" style={{ gap: theme.space[2] }}>
      {REMOVAL_REASONS.map(option => <Pressable key={option} testID={`stocking-reason-${option}`} accessibilityRole="radio" accessibilityState={{ selected: reason === option }}
        onPress={() => setReason(option)} style={{ minHeight: minTouchTarget, justifyContent: "center", paddingHorizontal: theme.space[3], borderRadius: theme.radius.md, borderWidth: 1,
          borderColor: reason === option ? theme.colors.accent : theme.colors.border, backgroundColor: reason === option ? theme.colors.accentWeak : theme.colors.surface }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`stocking.reason.${option}`)}</Text>
      </Pressable>)}
    </View>
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("stocking.removedOn")}</Text>
    <TextInput testID="stocking-removed-on" accessibilityLabel={t("stocking.removedOn")} placeholder={t("stocking.dayPlaceholder")} placeholderTextColor={theme.colors.muted}
      value={day} editable={!busy} inputMode="numeric" autoCapitalize="none" autoCorrect={false} onChangeText={setDay}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
        borderColor: problem ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
    {problem ? <Text testID="stocking-remove-problem" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t(problem)}</Text> : null}
    {error ? <Text testID="stocking-remove-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="stocking-remove-confirm" label={t("stocking.removeConfirm")} busy={busy} disabled={reason === null || problem !== null} onPress={() => void remove()} />
    <Button testID="stocking-remove-cancel" variant="secondary" label={t("navigation.cancel")} disabled={busy} onPress={onCancel} />
  </>;
}
