import { useState } from "react";
import { Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { RHYTHM_CHOICES, parseRhythm, type CareKind, type CarePlanRule } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { ReminderSettings } from "../reminders/ReminderSettings";
import { minTouchTarget, useTheme } from "../theme";
import { dueNote, dueText, ruleText } from "./carePlanText";
import { useCarePlan } from "./useCarePlan";

/** The care plan of an own tank: each rule with its due state and reason, and the controls to change its rhythm or switch it off. */
export function CarePlanView({ tankId, revision }: { tankId: number; revision: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { plan, error, saving, savingReminders, change, setReminders } = useCarePlan(tankId, true, revision);
  const [editing, setEditing] = useState<CareKind | null>(null);
  if (!plan && !error) return null;
  return <View testID="care-plan" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("care.plan.title")}</Text>
    {plan ? <ReminderSettings enabled={plan.remindersEnabled} busy={savingReminders} onToggle={() => void setReminders(!plan.remindersEnabled)} /> : null}
    {plan?.rules.map(rule => <RuleCard key={rule.kind} rule={rule} busy={saving === rule.kind} editing={editing === rule.kind}
      onEdit={() => setEditing(editing === rule.kind ? null : rule.kind)}
      onChange={async request => { if (await change(rule.kind, request)) setEditing(null); }}
      onToggle={() => void change(rule.kind, { enabled: !rule.enabled })} />)}
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(error, t, "common.loadError")}</Text> : null}
  </View>;
}

function RuleCard({ rule, busy, editing, onEdit, onChange, onToggle }: {
  rule: CarePlanRule; busy: boolean; editing: boolean; onEdit: () => void;
  onChange: (request: { rhythmDays: number }) => Promise<void>; onToggle: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [custom, setCustom] = useState("");
  const kind = rule.kind;
  const reason = ruleText(rule, t);
  const note = rule.due ? dueNote(rule.due, t) : null;
  const parsed = parseRhythm(custom);
  const text = { color: theme.colors.fg };
  const muted = { color: theme.colors.muted };
  return <View testID={`care-plan-rule-${kind}`} style={{ gap: theme.space[2], padding: theme.space[3], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, text]}>{t(`care.plan.kind.${kind}`)}</Text>
    {rule.rhythmDays === null
      ? <Text testID={`care-plan-state-${kind}`} style={[theme.type.body, muted]}>{t("care.plan.noRule")}</Text>
      : rule.due
        ? <Text testID={`care-plan-due-${kind}`} accessibilityLiveRegion="polite" style={[theme.type.bodyStrong, text]}>{dueText(rule.due, t)}</Text>
        : <Text testID={`care-plan-state-${kind}`} style={[theme.type.body, muted]}>{t("care.plan.switchedOff")}</Text>}
    {note ? <Text testID={`care-plan-note-${kind}`} style={[theme.type.meta, muted]}>{note}</Text> : null}
    {reason ? <Text testID={`care-plan-reason-${kind}`} style={[theme.type.body, text]}>{reason}</Text> : null}
    <Button testID={`care-plan-edit-${kind}`} variant="secondary" disabled={busy} label={t(rule.rhythmDays === null ? "care.plan.add" : "care.plan.edit")} onPress={onEdit} />
    {rule.rhythmDays !== null
      ? <Button testID={`care-plan-toggle-${kind}`} variant="secondary" busy={busy} label={t(rule.enabled ? "care.plan.off" : "care.plan.on")} onPress={onToggle} />
      : null}
    {editing ? <View testID={`care-plan-editor-${kind}`} style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.body, text]}>{t("care.plan.choicesLabel")}</Text>
      <View accessibilityRole="radiogroup" style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
        {RHYTHM_CHOICES.map(days => <View key={days} style={{ minWidth: 72, flexGrow: 1 }}>
          <Button testID={`care-plan-choice-${kind}-${days}`} variant={rule.rhythmDays === days ? "primary" : "secondary"} disabled={busy}
            label={String(days)} onPress={() => void onChange({ rhythmDays: days })} />
        </View>)}
      </View>
      <Text style={[theme.type.body, text]}>{t("care.plan.customLabel")}</Text>
      <TextInput testID={`care-plan-custom-${kind}`} accessibilityLabel={t("care.plan.customLabel")} value={custom} editable={!busy}
        onChangeText={setCustom} inputMode="numeric"
        style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface2, minHeight: minTouchTarget, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
      {custom.trim() !== "" && parsed === null ? <Text accessibilityRole="alert" style={[theme.type.meta, muted]}>{t("care.plan.customInvalid")}</Text> : null}
      <Button testID={`care-plan-custom-save-${kind}`} label={t("care.plan.saveCustom")} busy={busy} disabled={parsed === null} onPress={() => parsed !== null && void onChange({ rhythmDays: parsed })} />
      <Button testID={`care-plan-close-${kind}`} variant="secondary" label={t("care.plan.close")} onPress={onEdit} />
    </View> : null}
  </View>;
}
