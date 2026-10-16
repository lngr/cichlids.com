import { useEffect, useState } from "react";
import { ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import {
  WATER_CHANGE_STAGES, drainingSeconds, formatElapsed, totalSeconds, type TankDetail, type WaterChangeStage, type WaterChangeStep,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { amountDraft, parseCareAmount, type AmountUnit } from "../tanks/careAmount";
import { useTheme } from "../theme";
import { formatMeasurement } from "../units/format";
import { useUnits } from "../units/useUnits";
import { WaterChangeCamera } from "./WaterChangeCamera";
import { WaterChangeSummary } from "./WaterChangeSummary";
import { stageOfValue } from "./waterChangeText";
import { useWaterChange } from "./useWaterChange";

/**
 * The full-screen guided water change of an own tank: one calm step at a time with large controls,
 * the running timers and, at the end, the summary with the evidence state.
 */
export function WaterChangeScreen({ tankId }: { tankId: number }) {
  const { status } = useAuth();
  const router = useMachineRouter();
  useEffect(() => {
    if (status === "anonymous") router.replace("/me");
  }, [status, router]);
  if (status === "loading" || status === "anonymous") return <LoadingView />;
  return <Wizard tankId={tankId} />;
}

function Wizard({ tankId }: { tankId: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const router = useMachineRouter();
  const { actor, state } = useWaterChange(tankId);
  const { context } = state;
  const stage = stageOfValue(state.value);
  const [tank, setTank] = useState<TankDetail | null>(null);
  const [confirmCancel, setConfirmCancel] = useState(false);
  useEffect(() => {
    let cancelled = false;
    void apiClient.tanks.get(tankId).then(value => { if (!cancelled) setTank(value); }).catch(() => {});
    return () => { cancelled = true; };
  }, [tankId]);
  const leaveTo = `/tanks/${tankId}?view=care`;
  const done = state.status === "done";
  const finished = done ? state.output.finished : null;
  useEffect(() => {
    if (done && !finished) router.dismissTo(leaveTo);
  }, [done, finished, router, leaveTo]);

  const minMinutes = Math.round(Number(context.session?.minDurationSeconds ?? 600) / 60);
  const page = { width: "100%" as const, maxWidth: 560, alignSelf: "center" as const, gap: theme.space[4] };
  const container = { backgroundColor: theme.colors.bg };
  const text = { color: theme.colors.fg };
  const muted = { color: theme.colors.muted };

  if (state.matches("starting") || state.matches("routing")) return <LoadingView label={t("waterChange.loading")} />;
  if (state.matches("failed")) return <ErrorView message={translateApiError(context.error, t, "waterChange.startError")} onRetry={() => actor.send({ type: "RETRY" })} />;
  if (finished) {
    return <ScrollView testID="water-change-screen" style={container} contentContainerStyle={styles.content}>
      <View style={page}><WaterChangeSummary finished={finished} minMinutes={minMinutes} onClose={() => router.dismissTo(leaveTo)} /></View>
    </ScrollView>;
  }
  if (!stage) return <LoadingView label={t("waterChange.loading")} />;

  const index = WATER_CHANGE_STAGES.indexOf(stage);
  const busy = state.matches("finishing") || state.matches("cancelling");
  const errorText = context.error ? translateApiError(context.error, t, stage === "amount" ? "waterChange.amount.finishError" : "waterChange.camera.uploadError") : null;
  const timer = (testID: string, label: string, seconds: number) => <View testID={testID} accessible accessibilityRole="timer"
    accessibilityLabel={t("waterChange.timerLabel", { label, time: formatElapsed(seconds) })} style={{ gap: theme.space[1] }}>
    <Text style={[theme.type.meta, muted]}>{label}</Text>
    <Text style={[styles.timer, text]}>{formatElapsed(seconds)}</Text>
  </View>;
  const stageText = (key: string) => t(`waterChange.stage.${stage}.${key}`);
  const step = (primary: string, skip: string = "waterChange.skip") => <View style={{ gap: theme.space[3] }}>
    <Button testID="water-change-done" label={primary} onPress={() => actor.send({ type: "DONE" })} />
    <Button testID="water-change-skip" variant="secondary" label={t(skip)} onPress={() => actor.send({ type: "SKIP" })} />
  </View>;

  return <ScrollView testID="water-change-screen" style={container} contentContainerStyle={styles.content}>
    <View style={page}>
      <Text testID="water-change-progress" style={[theme.type.meta, muted]}>
        {t("waterChange.stepOf", { current: index + 1, total: WATER_CHANGE_STAGES.length })}
      </Text>
      <View style={[styles.timers, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
        {timer("water-change-timer", t("waterChange.timerTotal"), totalSeconds(context))}
        {context.drainStartedAtMs !== null ? timer("water-change-drain-timer", t("waterChange.timerDrain"), drainingSeconds(context)) : null}
      </View>
      <Text accessibilityRole="header" style={[theme.type.display, text]}>{stageText("title")}</Text>
      <Text style={[theme.type.body, text]}>{stageText("body")}</Text>

      {stage === "heaterFilterOff" || stage === "draining" || stage === "refill" || stage === "heaterFilterOn" ? step(stageText("done")) : null}

      {stage === "drainedPhoto" || stage === "refilledPhoto" ? <WaterChangeCamera
        overlay={stage === "drainedPhoto" && context.session?.previousDrainedPhoto
          ? apiClient.care.photoUrl(context.session.previousDrainedPhoto.large ?? context.session.previousDrainedPhoto.medium ?? context.session.previousDrainedPhoto.thumb ?? "") : null}
        uploading={state.matches({ running: { [stage]: "uploading" } })} error={errorText}
        onUse={file => actor.send({ type: "PHOTO", file, capturedInApp: true })} onSkip={() => actor.send({ type: "SKIP" })} /> : null}

      {stage === "tips" ? <View style={{ gap: theme.space[3] }}>
        {(["conditioner", "temperature_matched"] as const satisfies readonly WaterChangeStep[]).map(tip => {
          const chosen = context.tips[tip] === "done";
          return <Button key={tip} testID={`water-change-tip-${tip}`} variant={chosen ? "primary" : "secondary"} label={t(`waterChange.tip.${tip}`)}
            accessibilityLabel={t(chosen ? "waterChange.tip.checked" : "waterChange.tip.unchecked", { tip: t(`waterChange.tip.${tip}`) })}
            onPress={() => actor.send({ type: "TIP", step: tip, state: chosen ? "skipped" : "done" })} />;
        })}
        {step(stageText("done"), "waterChange.stage.tips.skip")}
      </View> : null}

      {stage === "amount" ? <AmountStep tank={tank} busy={busy} error={errorText} system={system}
        onChange={amount => actor.send({ type: "SET_AMOUNT", amount })} onFinish={() => actor.send({ type: "FINISH" })} /> : null}

      <View style={{ gap: theme.space[2] }}>
        {confirmCancel ? <View testID="water-change-cancel-confirm" style={{ gap: theme.space[2] }}>
          <Text accessibilityRole="alert" style={[theme.type.body, text]}>{t("waterChange.cancel.confirm")}</Text>
          <Button testID="water-change-cancel-yes" variant="secondary" label={t("waterChange.cancel.yes")} disabled={busy} onPress={() => actor.send({ type: "CANCEL" })} />
          <Button testID="water-change-cancel-no" variant="secondary" label={t("waterChange.cancel.no")} onPress={() => setConfirmCancel(false)} />
        </View> : <Button testID="water-change-cancel" variant="secondary" label={t("waterChange.cancel.action")} disabled={busy} onPress={() => setConfirmCancel(true)} />}
      </View>
    </View>
  </ScrollView>;
}

function AmountStep({ tank, busy, error, system, onChange, onFinish }: {
  tank: TankDetail | null; busy: boolean; error: string | null; system: "metric" | "us";
  onChange: (amount: ReturnType<typeof parseCareAmount>) => void; onFinish: () => void;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [draft, setDraft] = useState(() => amountDraft(null, system));
  const parsed = parseCareAmount(draft.value, draft.unit);
  const invalid = draft.value.trim() !== "" && !parsed;
  useEffect(() => { onChange(parsed); }, [parsed?.value, parsed?.unit]);
  const liters = tank?.volume ? Number(tank.volume.liters) : null;
  const holds = liters !== null ? t("waterChange.amount.tankVolume", { volume: formatMeasurement(liters, "volume", system, i18n.language) }) : null;
  const reference = parsed?.unit === "percent" && liters !== null
    ? t("waterChange.amount.about", { volume: formatMeasurement(liters * Number(parsed.value) / 100, "volume", system, i18n.language) }) : null;
  return <View style={{ gap: theme.space[3] }}>
    {holds ? <Text testID="water-change-tank-volume" style={[theme.type.body, { color: theme.colors.muted }]}>{holds}</Text> : null}
    <TextInput testID="water-change-amount-input" accessibilityLabel={t("care.amount")} value={draft.value} editable={!busy} inputMode="decimal"
      onChangeText={value => setDraft(current => ({ ...current, value }))}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: 56, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
    <View style={{ flexDirection: "row", gap: theme.space[2] }}>
      {(["percent", "liter", "us_gallon"] as AmountUnit[]).map(unit => <View key={unit} style={{ flex: 1 }}>
        <Button testID={`water-change-unit-${unit}`} variant={draft.unit === unit ? "primary" : "secondary"} disabled={busy} label={t(`care.${unit}`)}
          onPress={() => setDraft(current => ({ ...current, unit }))} />
      </View>)}
    </View>
    {reference ? <Text testID="water-change-amount-reference" style={[theme.type.body, { color: theme.colors.muted }]}>{reference}</Text> : null}
    {invalid ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("waterChange.amount.invalid")}</Text> : null}
    {error ? <Text accessibilityRole="alert" testID="water-change-error" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="water-change-finish" label={t(busy ? "waterChange.amount.finishing" : "waterChange.amount.finish")} busy={busy} disabled={invalid} onPress={onFinish} />
  </View>;
}

const styles = StyleSheet.create({
  content: { padding: 16, paddingBottom: 48 },
  timers: { borderWidth: 1, padding: 16, gap: 12 },
  timer: { fontSize: 36, fontWeight: "800", fontVariant: ["tabular-nums"] },
});
