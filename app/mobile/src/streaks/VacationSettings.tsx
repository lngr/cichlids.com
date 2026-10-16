import { useCallback, useState } from "react";
import { Text, TextInput, View } from "react-native";
import { useFocusEffect } from "expo-router";
import { useTranslation } from "react-i18next";
import { activeVacation, checkVacationDates, type Vacation } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { localCalendarDate } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { formatLocalDate, pastVacationLines } from "./streakText";

const PAST_VACATIONS_SHOWN = 5;

/**
 * The vacation mode in the settings of the member: start a vacation now, end the active one, or
 * record a past vacation afterwards. An active vacation pauses the streaks of all tanks and is
 * private until it ends.
 */
export function VacationSettings() {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [vacations, setVacations] = useState<Vacation[] | null>(null);
  const [recording, setRecording] = useState(false);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useFocusEffect(useCallback(() => {
    let active = true;
    void apiClient.me.vacations().then(result => { if (active) setVacations(result); }).catch(() => { if (active) setVacations(null); });
    return () => { active = false; };
  }, [attempt]));
  if (!vacations) return null;
  const active = activeVacation(vacations);
  const today = localCalendarDate();
  const check = recording ? checkVacationDates(from, to, today) : "ok";
  const problem = from === "" && to === "" ? null : check === "ok" ? null : check;
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      setRecording(false);
      setFrom("");
      setTo("");
      setAttempt(value => value + 1);
    } catch (err) {
      setError(translateApiError(err, t, "common.loadError"));
    } finally {
      setBusy(false);
    }
  };
  const field = (invalid: boolean) => [theme.type.body, {
    color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
    borderColor: invalid ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3],
  }];
  const past = pastVacationLines(vacations, t, i18n.language, PAST_VACATIONS_SHOWN);
  return <View testID="vacation-settings" style={{ gap: theme.space[3], paddingVertical: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("streaks.vacation.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("streaks.vacation.intro")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("streaks.vacation.privacy")}</Text>
    <Text testID="vacation-status" accessibilityLiveRegion="polite" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>
      {active ? t("streaks.vacation.active", { date: formatLocalDate(active.startDate, i18n.language) }) : t("streaks.vacation.none")}</Text>
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    {active
      ? <Button testID="vacation-end" label={t("streaks.vacation.end")} busy={busy} onPress={() => void run(() => apiClient.me.endVacation(today))} />
      : <Button testID="vacation-start" label={t("streaks.vacation.start")} busy={busy} onPress={() => void run(() => apiClient.me.startVacation(today))} />}
    {recording ? <View testID="vacation-record-form" style={{ gap: theme.space[2] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("streaks.vacation.record")}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("streaks.vacation.from")}</Text>
      <TextInput testID="vacation-from" accessibilityLabel={t("streaks.vacation.from")} placeholder={t("care.when.datePlaceholder")} placeholderTextColor={theme.colors.muted}
        value={from} editable={!busy} inputMode="numeric" autoCapitalize="none" autoCorrect={false} onChangeText={setFrom} style={field(problem !== null)} />
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("streaks.vacation.to")}</Text>
      <TextInput testID="vacation-to" accessibilityLabel={t("streaks.vacation.to")} placeholder={t("care.when.datePlaceholder")} placeholderTextColor={theme.colors.muted}
        value={to} editable={!busy} inputMode="numeric" autoCapitalize="none" autoCorrect={false} onChangeText={setTo} style={field(problem !== null)} />
      {problem ? <Text testID="vacation-record-error" accessibilityRole="alert" style={[theme.type.meta, { color: theme.colors.danger }]}>
        {t(problem === "future" ? "streaks.vacation.errorFuture" : problem === "order" ? "streaks.vacation.errorOrder" : "streaks.vacation.errorInvalid")}</Text> : null}
      <Button testID="vacation-save" label={t("streaks.vacation.save")} busy={busy} disabled={check !== "ok" || from === "" || to === ""}
        onPress={() => void run(() => apiClient.me.recordVacation(today, from.trim(), to.trim()))} />
      <Button testID="vacation-cancel" variant="secondary" label={t("streaks.vacation.cancel")} disabled={busy} onPress={() => { setRecording(false); setError(null); }} />
    </View> : <Button testID="vacation-record" variant="secondary" label={t("streaks.vacation.record")} onPress={() => setRecording(true)} />}
    {past.length > 0 ? <View testID="vacation-past" style={{ gap: theme.space[1] }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("streaks.vacation.past")}</Text>
      {past.map((line, index) => <Text key={index} style={[theme.type.body, { color: theme.colors.muted }]}>{line}</Text>)}
    </View> : null}
  </View>;
}
