import { Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { customFromInstant, deviceTimeZone, resolveWhen, type WhenChoice } from "@cichlids/client-core";
import { Chip } from "../components/Chip";
import { minTouchTarget, useTheme } from "../theme";
import { formatEntryTime } from "./measurementFormat";

const CHOICES = ["now", "yesterday", "twoDaysAgo", "custom"] as const;

/**
 * The choice of when a care entry happened: now, yesterday, two days ago, or a date and time typed
 * in the local time zone. An existing entry also offers keeping its time. The chosen time is never
 * in the future.
 */
export function WhenPicker({ value, onChange, title, prefill, existing = false, disabled = false, testIDPrefix = "when" }: {
  value: WhenChoice;
  onChange: (value: WhenChoice) => void;
  title?: string;
  /** The instant a chosen date and time starts from. */
  prefill: Date;
  /** Whether the choice is for an existing entry, which offers keeping its time instead of now. */
  existing?: boolean;
  disabled?: boolean;
  testIDPrefix?: string;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const zone = deviceTimeZone();
  const resolved = resolveWhen(value, new Date(), zone);
  const kinds = existing ? (["keep", "yesterday", "twoDaysAgo", "custom"] as const) : CHOICES;
  const choose = (kind: (typeof kinds)[number]) => {
    if (disabled) return;
    onChange(kind === "custom" ? customFromInstant(prefill, zone) : { kind });
  };
  const field = (problem: boolean) => [theme.type.body, {
    color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
    borderColor: problem ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3],
  }];
  return <View testID={testIDPrefix} style={{ gap: theme.space[2] }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{title ?? t("care.when.title")}</Text>
    <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
      {kinds.map(kind => <Chip key={kind} testID={`${testIDPrefix}-${kind}`} label={t(`care.when.${kind}`)} active={value.kind === kind} onPress={() => choose(kind)} />)}
    </View>
    {value.kind === "custom" ? <View style={{ flexDirection: "row", gap: theme.space[2] }}>
      <View style={{ flex: 3, gap: theme.space[1] }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.when.date")}</Text>
        <TextInput testID={`${testIDPrefix}-date`} accessibilityLabel={t("care.when.dateLabel")} placeholder={t("care.when.datePlaceholder")} placeholderTextColor={theme.colors.muted}
          value={value.date} editable={!disabled} inputMode="numeric" autoCapitalize="none" autoCorrect={false}
          onChangeText={date => onChange({ ...value, date })} style={field(resolved.error !== null)} />
      </View>
      <View style={{ flex: 2, gap: theme.space[1] }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.when.time")}</Text>
        <TextInput testID={`${testIDPrefix}-time`} accessibilityLabel={t("care.when.timeLabel")} placeholder={t("care.when.timePlaceholder")} placeholderTextColor={theme.colors.muted}
          value={value.time} editable={!disabled} inputMode="numeric" autoCapitalize="none" autoCorrect={false}
          onChangeText={time => onChange({ ...value, time })} style={field(resolved.error !== null)} />
      </View>
    </View> : null}
    {existing && value.kind === "keep" ? <Text testID={`${testIDPrefix}-current`} style={[theme.type.meta, { color: theme.colors.muted }]}>
      {t("care.when.current", { time: formatEntryTime(prefill.toISOString(), i18n.language) })}</Text> : null}
    {resolved.error ? <Text testID={`${testIDPrefix}-error`} accessibilityRole="alert" style={[theme.type.meta, { color: theme.colors.danger }]}>
      {t(resolved.error === "future" ? "care.when.errorFuture" : "care.when.errorInvalid")}</Text> : null}
    {resolved.occurredAt && value.kind !== "custom" ? <Text testID={`${testIDPrefix}-preview`} style={[theme.type.meta, { color: theme.colors.muted }]}>
      {t("care.when.preview", { time: formatEntryTime(resolved.occurredAt, i18n.language) })}</Text> : null}
  </View>;
}
