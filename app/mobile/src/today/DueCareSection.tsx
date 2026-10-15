import { useRef } from "react";
import { Modal, ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { DueCareItem } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { MeasurementForm } from "../tanks/MeasurementForm";
import { dueText } from "../tanks/carePlanText";
import { useCareActors } from "../tanks/useCareActors";
import { useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { useDueCare } from "./useDueCare";

/**
 * The due care of all own tanks with a one-tap action per item: a water change is logged right
 * away with a confirmation and an undo, a measurement opens the measurement form of its tank.
 * The section is absent while nothing is due.
 */
export function DueCareSection() {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const target = useRef(0);
  const { actor, state, measureActor, measureState } = useCareActors(() => target.current);
  const items = useDueCare(state.context.revision + measureState.context.revision);
  const confirming = state.matches("confirmed") || state.matches("undoing");
  const measuring = !measureState.matches("idle");
  const busy = state.matches("logging") || state.matches("undoing") || measuring;
  const error = state.context.error;
  const act = (item: DueCareItem) => {
    target.current = Number(item.tankId);
    if (item.kind === "measurement") measureActor.send({ type: "OPEN", system });
    else actor.send({ type: "LOG" });
  };
  return <>
    {items.length > 0 || confirming ? <View testID="today-due-care" style={{ gap: theme.space[3] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("today.due.title")}</Text>
      {confirming ? <View testID="today-due-confirmation" style={{ gap: theme.space[2] }}>
        <Text accessibilityLiveRegion="polite" style={[theme.type.body, { color: theme.colors.fg }]}>{t("care.logged")}</Text>
        <Button testID="today-due-undo" variant="secondary" label={t("care.undo")} busy={state.matches("undoing")} onPress={() => actor.send({ type: "UNDO" })} />
      </View> : null}
      {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(error, t, "common.loadError")}</Text> : null}
      {items.map(item => {
        const water = item.kind === "water_change";
        return <View key={`${item.tankId}-${item.kind}`} testID={`today-due-${item.tankId}-${item.kind}`}
          style={{ gap: theme.space[2], padding: theme.space[4], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{item.tankTitle || t("common.untitled")}</Text>
          <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(`care.plan.kind.${item.kind}`)}</Text>
          <Text style={[theme.type.body, { color: theme.colors.muted }]}>{dueText(item.due, t)}</Text>
          <Button testID={water ? `today-due-log-${item.tankId}` : `today-due-measure-${item.tankId}`} variant="secondary" disabled={busy}
            label={t(water ? "today.due.logWaterChange" : "care.measure.button")}
            accessibilityLabel={t(water ? "today.due.logWaterChangeFor" : "today.due.measureFor", { tank: item.tankTitle || t("common.untitled") })}
            onPress={() => act(item)} />
        </View>;
      })}
    </View> : null}
    <Modal visible={measuring} transparent animationType="slide" onRequestClose={() => measureActor.send({ type: "CANCEL" })}>
      <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
        <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[4] }}>
          {measuring ? <MeasurementForm stream={{ measure: { state: measureState, send: measureActor.send } }} /> : null}
        </ScrollView>
      </View>
    </Modal>
  </>;
}
