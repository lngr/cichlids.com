import { StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import { formatElapsed, type WaterChangeFinished } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { formatCareAmount } from "../tanks/careAmount";
import { evidenceKey, missingKey } from "./waterChangeText";

type Photo = NonNullable<WaterChangeFinished["session"]["drainedPhoto"]>;

/**
 * The end of a guided water change: its duration, the amount, the photos that stay private and
 * the evidence state with calm wording.
 */
export function WaterChangeSummary({ finished, minMinutes, onClose }: { finished: WaterChangeFinished; minMinutes: number; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const { entry, session } = finished;
  const evidence = entry.evidence;
  const photo = (value: Photo | null, label: string) => {
    const path = value?.urls.medium ?? value?.urls.small ?? value?.urls.thumb ?? value?.urls.large;
    return path ? <View key={label} style={{ flex: 1, gap: theme.space[1] }}>
      <Image source={apiClient.care.photoUrl(path)} accessibilityLabel={label} contentFit="cover"
        style={{ width: "100%", aspectRatio: 3 / 4, borderRadius: theme.radius.md, backgroundColor: theme.colors.placeholder }} />
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{label}</Text>
    </View> : null;
  };
  const photos = [photo(session.drainedPhoto, t("waterChange.summary.drained")), photo(session.refilledPhoto, t("waterChange.summary.refilled"))].filter(Boolean);
  return <View testID="water-change-summary" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("waterChange.summary.title")}</Text>
    <Text testID="water-change-duration" style={[theme.type.body, { color: theme.colors.fg }]}>
      {t("waterChange.summary.duration", { time: formatElapsed(Number(entry.durationSeconds ?? 0)) })}
    </Text>
    {entry.amount ? <Text testID="water-change-summary-amount" style={[theme.type.body, { color: theme.colors.fg }]}>
      {t("waterChange.summary.amount", { amount: formatCareAmount(entry.amount, i18n.language) })}
    </Text> : null}
    {evidence ? <View style={[styles.card, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg }]}>
      <Text testID="water-change-evidence" accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(evidenceKey(evidence.state))}</Text>
      {evidence.missing.map(code => missingKey(code) ? <Text key={code} testID="water-change-missing" style={[theme.type.body, { color: theme.colors.muted }]}>
        {t(missingKey(code)!, { minutes: minMinutes })}</Text> : null)}
    </View> : null}
    {photos.length > 0 ? <>
      <View style={{ flexDirection: "row", gap: theme.space[3] }}>{photos}</View>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("waterChange.summary.private")}</Text>
    </> : null}
    <Button testID="water-change-close" label={t("waterChange.summary.close")} onPress={onClose} />
  </View>;
}

const styles = StyleSheet.create({ card: { borderWidth: 1, padding: 16, gap: 8 } });
