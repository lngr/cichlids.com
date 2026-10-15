import { useState } from "react";
import { Modal, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { CareEntry } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { translateApiError } from "../lib/apiErrors";
import { amountDraft, parseCareAmount, formatCareAmount, type AmountUnit } from "./careAmount";
import type { CareStream } from "./useCareStream";

export function CareStreamView({ stream }: { stream: CareStream }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const { state, send, list, entries, owner } = stream;
  const text = { color: theme.colors.fg };
  const busy = state.matches("logging") || state.matches("undoing") || state.matches("saving") || state.matches("deleting");
  const editing = state.matches("editing") || state.matches("saving") || state.matches("deleting");
  return <View style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("tankStory.logs")}</Text>
    {owner ? <>
      <Button testID="care-water-change" label={t("care.waterChange")} busy={busy} disabled={editing} onPress={() => send({ type: "LOG" })} />
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.oneTap")}</Text>
      {state.matches("confirmed") ? <View testID="care-confirmation" style={{ gap: theme.space[2] }}>
        <Text accessibilityLiveRegion="polite" style={[theme.type.body, text]}>{t("care.logged")}</Text>
        <Button testID="care-undo" variant="secondary" label={t("care.undo")} onPress={() => send({ type: "UNDO" })} />
      </View> : null}
      {state.context.error && !editing ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(state.context.error, t, "common.loadError")}</Text> : null}
      {list.loading ? <Text style={[theme.type.body, text]}>{t("common.loading")}</Text> : null}
      {list.error ? <>
        <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{list.error}</Text>
        <Button variant="secondary" label={t("common.retry")} onPress={list.reload} />
      </> : null}
      {entries.map(entry => <View key={String(entry.id)} testID={`care-entry-${entry.id}`} style={{ paddingVertical: theme.space[3], gap: theme.space[2], borderBottomWidth: 1, borderColor: theme.colors.border }}>
        <Text style={[theme.type.bodyStrong, text]}>{t("care.waterChange")}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" }).format(new Date(entry.occurredAt))}</Text>
        {entry.amount ? <Text testID="care-entry-amount" style={[theme.type.body, text]}>{formatCareAmount(entry.amount, i18n.language)}</Text> : null}
        <Button testID={`care-edit-${entry.id}`} variant="secondary" label={t(entry.amount ? "care.editAmount" : "care.addAmount")} disabled={busy || editing} onPress={() => send({ type: "EDIT", entry })} />
      </View>)}
      {list.hasMore ? <Button testID="care-more" variant="secondary" label={t("care.more")} busy={list.loadingMore} onPress={list.loadMore} /> : null}
      <Modal visible={editing} transparent animationType="slide" onRequestClose={() => send({ type: "CANCEL" })}>
        <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
          <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[4] }}>
            {state.context.entry && editing ? <AmountEditor key={String(state.context.entry.id)} entry={state.context.entry} stream={stream} /> : null}
          </ScrollView>
        </View>
      </Modal>
    </> : null}
    {(!owner || !list.loading && !list.error && entries.length === 0) ? <Text testID="tank-care-empty" style={[theme.type.body, text]}>{t("tankStory.logsEmpty")}</Text> : null}
  </View>;
}
function AmountEditor({ entry, stream }: { entry: CareEntry; stream: CareStream }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const [draft, setDraft] = useState(() => amountDraft(entry.amount, system));
  const { state, send } = stream;
  const busy = !state.matches("editing");
  const parsed = parseCareAmount(draft.value, draft.unit);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("care.editAmount")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("care.amount")}</Text>
    <TextInput testID="care-amount-input" accessibilityLabel={t("care.amount")} value={draft.value} editable={!busy}
      onChangeText={value => setDraft(current => ({ ...current, value }))} inputMode="decimal"
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: 48, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
    <View style={{ flexDirection: "row", gap: theme.space[2] }}>
      {(["liter", "us_gallon", "percent"] as AmountUnit[]).map(unit => <View key={unit} style={{ flex: 1 }}>
        <Button testID={`care-unit-${unit}`} variant={draft.unit === unit ? "primary" : "secondary"} disabled={busy} label={t(`care.${unit}`)} onPress={() => setDraft(current => ({ ...current, unit }))} />
      </View>)}
    </View>
    {state.context.error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(state.context.error, t, "common.loadError")}</Text> : null}
    <Button testID="care-amount-save" label={t("care.save")} busy={state.matches("saving")} disabled={!parsed || busy} onPress={() => parsed && send({ type: "SAVE", amount: parsed })} />
    {entry.amount ? <Button testID="care-amount-clear" variant="secondary" disabled={busy} label={t("care.clearAmount")} onPress={() => send({ type: "SAVE", amount: null })} /> : null}
    <Button testID="care-delete" variant="secondary" busy={state.matches("deleting")} disabled={busy} label={t("care.delete")} onPress={() => send({ type: "DELETE" })} />
    <Button testID="care-amount-cancel" variant="secondary" disabled={busy} label={t("navigation.cancel")} onPress={() => send({ type: "CANCEL" })} />
  </>;
}
