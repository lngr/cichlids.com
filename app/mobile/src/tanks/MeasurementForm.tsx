import { Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { inputUnit, validateDraft, visibleParameters, MEASUREMENT_ORIGINS } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { translateApiError } from "../lib/apiErrors";
import { minTouchTarget, useTheme } from "../theme";
import { measurementUnitSymbol } from "./measurementFormat";
import type { CareStream } from "./useCareStream";

/** The progressive form of a measurement: the common parameters first, more on demand, one source for all values. */
export function MeasurementForm({ stream }: { stream: CareStream }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const { state, send } = stream.measure;
  const { draft, entry, error } = state.context;
  const busy = !state.matches("editing");
  const { values, errors } = validateDraft(draft);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(entry ? "care.measure.editTitle" : "care.measure.formTitle")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("care.measure.hint")}</Text>
    {visibleParameters(draft).map(parameter => {
      const symbol = measurementUnitSymbol(inputUnit(parameter, draft.system), i18n.language);
      const label = t(`care.measure.parameters.${parameter}`) + (symbol ? ` (${symbol})` : "");
      const problem = draft.fields[parameter]?.trim() ? errors[parameter] : undefined;
      return <View key={parameter} style={{ gap: theme.space[1] }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{label}</Text>
        <TextInput testID={`measure-input-${parameter}`} accessibilityLabel={label} value={draft.fields[parameter] ?? ""} editable={!busy}
          onChangeText={text => send({ type: "CHANGE", parameter, text })} inputMode="decimal"
          style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1, borderColor: problem ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
        {problem ? <Text accessibilityRole="alert" style={[theme.type.meta, { color: theme.colors.danger }]}>{t(problem === "range" ? "care.measure.errorRange" : "care.measure.errorInvalid")}</Text> : null}
      </View>;
    })}
    {!draft.expanded ? <Button testID="measure-more" variant="secondary" disabled={busy} label={t("care.measure.more")} onPress={() => send({ type: "MORE" })} /> : null}
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("care.measure.origin")}</Text>
    <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
      <Chip testID="measure-origin-none" label={t("care.measure.originNone")} active={draft.origin === null} onPress={() => send({ type: "ORIGIN", origin: null })} />
      {MEASUREMENT_ORIGINS.map(origin => <Chip key={origin} testID={`measure-origin-${origin}`} label={t(`care.measure.origins.${origin}`)}
        active={draft.origin === origin} onPress={() => send({ type: "ORIGIN", origin })} />)}
    </View>
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{translateApiError(error, t, "common.loadError")}</Text> : null}
    <Button testID="measure-save" label={t("care.measure.save")} busy={state.matches("saving")} disabled={!values || busy} onPress={() => send({ type: "SAVE" })} />
    {entry ? <Button testID="measure-delete" variant="secondary" busy={state.matches("deleting")} disabled={busy} label={t("care.measure.delete")} onPress={() => send({ type: "DELETE" })} /> : null}
    <Button testID="measure-cancel" variant="secondary" disabled={busy} label={t("navigation.cancel")} onPress={() => send({ type: "CANCEL" })} />
  </>;
}
