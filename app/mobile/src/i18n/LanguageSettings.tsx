import { useState } from "react";
import { Pressable, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { Button } from "../components/Button";
import { formatDate } from "../lib/format";
import { languagePreference, pseudoRTL, setLanguagePreference, setPseudoRTLPreference, SUPPORTED_LANGUAGES, type SupportedLanguage } from "./index";

export function LanguageSettings() {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const [selected, setSelected] = useState(languagePreference);
  const [rtl, setRTL] = useState(pseudoRTL);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);

  async function select(language: SupportedLanguage | null) {
    setBusy(true);
    setError(false);
    try {
      await setLanguagePreference(language);
      setSelected(language);
      setExpanded(false);
    } catch { setError(true); }
    finally { setBusy(false); }
  }

  return <View style={{ padding: theme.space[4], gap: theme.space[2], alignSelf: "stretch" }}>
    <Pressable testID="language-settings-toggle" accessibilityRole="button" accessibilityState={{ expanded }} onPress={() => setExpanded(!expanded)} style={{ minHeight: 48, justifyContent: "center" }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("settings.language")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{selected ? t(`settings.languages.${selected}`) : t("settings.device")}</Text>
    </Pressable>
    {expanded ? <View accessibilityRole="radiogroup" accessibilityLabel={t("settings.language")} style={{ gap: theme.space[1] }}>
      {([null, ...SUPPORTED_LANGUAGES] as const).map((language) => <Pressable key={language ?? "device"} testID={`language-${language ?? "device"}`} accessibilityRole="radio" accessibilityState={{ checked: selected === language, disabled: busy }} disabled={busy} onPress={() => void select(language)} style={{ minHeight: 48, padding: theme.space[3], borderRadius: theme.radius.sm, backgroundColor: selected === language ? theme.colors.accentWeak : theme.colors.surface }}>
        <Text style={[theme.type.body, { color: theme.colors.fg }]}>{language ? t(`settings.languages.${language}`) : t("settings.device")}</Text>
      </Pressable>)}
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("settings.formatPreview")}</Text>
      <Text testID="locale-format-preview" style={[theme.type.body, { color: theme.colors.fg }]}>{new Intl.NumberFormat(i18n.language).format(12345.6)}{" · "}{formatDate("2026-07-15T12:00:00Z", i18n.language)}</Text>
      {__DEV__ ? <Button testID="pseudo-rtl-toggle" variant="secondary" label={t("settings.pseudoRTL")} onPress={() => { void setPseudoRTLPreference(!rtl).then(() => setRTL(!rtl)).catch(() => setError(true)); }} /> : null}
    </View> : null}
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t("settings.saveError")}</Text> : null}
  </View>;
}
