import { useCallback, useEffect, useState } from "react";
import { ScrollView, Text } from "react-native";
import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import { localStore } from "../lib/localStore";
import { useTheme } from "../theme";
import { isDeviceNoteSeen, rememberDeviceNote } from "./deviceNote";
import { useRegistration } from "./RegistrationProvider";

/** Whether the note was shown: null while the answer is read, then the answer, and a function that remembers it. */
export function useDeviceNote(): { seen: boolean | null; remember(): void } {
  const [seen, setSeen] = useState<boolean | null>(null);
  useEffect(() => {
    let cancelled = false;
    void isDeviceNoteSeen(localStore).then((value) => { if (!cancelled) setSeen(value); });
    return () => { cancelled = true; };
  }, []);
  const remember = useCallback(() => {
    setSeen(true);
    void rememberDeviceNote(localStore);
  }, []);
  return { seen, remember };
}

/**
 * The note before the first tank of a provisional account: the account lives on this device until
 * the member registers, with the offer to register. Either button counts as having seen it.
 */
export function DeviceAccountNote({ onContinue }: { onContinue: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const { requestRegistration } = useRegistration();
  return <ScrollView testID="device-note" style={{ backgroundColor: theme.colors.bg }}
    contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: theme.space[6], gap: theme.space[4] }}>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("registration.note.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("registration.note.text")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("registration.note.loss")}</Text>
    <Button testID="device-note-continue" label={t("registration.note.continue")} onPress={onContinue} />
    <Button testID="device-note-register" variant="secondary" label={t("registration.note.register")}
      onPress={() => { onContinue(); requestRegistration("register"); }} />
  </ScrollView>;
}
