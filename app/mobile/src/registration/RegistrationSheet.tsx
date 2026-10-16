import { useEffect, useRef } from "react";
import { ActivityIndicator, Modal, Platform, Pressable, ScrollView, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import type { ProvisionalContent } from "@cichlids/client-core";
import { useLoginAction } from "../auth/useLoginAction";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useRegistration } from "./RegistrationProvider";
import { registrationErrorKey, type SheetState } from "./registrationSheet";

/**
 * The sheet that asks a provisional account to register. It names the reason, runs the Keycloak
 * sign-in or registration, and after the sign-in reports what happened to the content of the
 * device or asks whether to take it along.
 */
export function RegistrationSheet() {
  const { sheet, closeSheet } = useRegistration();
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { t } = useTranslation();
  const visible = sheet.step !== "closed";
  const focusBefore = useRef<HTMLElement | null>(null);
  const closable = sheet.step === "reason" || sheet.step === "done" || sheet.step === "error";

  useEffect(() => {
    if (Platform.OS !== "web" || !visible) return;
    focusBefore.current = document.activeElement as HTMLElement;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") closeSheet(); };
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("keydown", escape); focusBefore.current?.focus(); };
  }, [visible, closeSheet]);

  return <Modal visible={visible} transparent animationType="slide" onRequestClose={closeSheet}>
    <View style={{ flex: 1, justifyContent: "flex-end" }}>
      <Pressable accessibilityLabel={t("navigation.cancel")} disabled={!closable} onPress={closeSheet} style={{ flex: 1, backgroundColor: theme.colors.bg, opacity: 0.8 }} />
      <View testID="registration-sheet" accessibilityViewIsModal
        style={{ maxHeight: "90%", backgroundColor: theme.colors.surface, borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet }}>
        <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={{ padding: theme.space[6], paddingBottom: theme.space[6] + insets.bottom, gap: theme.space[4] }}>
          <SheetContent sheet={sheet} />
        </ScrollView>
      </View>
    </View>
  </Modal>;
}

function SheetContent({ sheet }: { sheet: SheetState }) {
  switch (sheet.step) {
    case "reason": return <ReasonStep reason={sheet.reason} />;
    case "working": return <WorkingStep />;
    case "decision": return <DecisionStep content={sheet.preview.content} dropped={sheet.preview.vacationsDropped + sheet.preview.vacationsDroppedRepair} />;
    case "done": return <DoneStep result={sheet.result} moved={sheet.moved} dropped={sheet.dropped} />;
    case "error": return <ErrorStep error={sheet.error} />;
    default: return null;
  }
}

function ReasonStep({ reason }: { reason: string }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { closeSheet } = useRegistration();
  const { busy, failure, onLogin } = useLoginAction();
  return <>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("registration.title")}</Text>
    <Text testID="registration-reason" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(`registration.reason.${reason}`)}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("registration.keep")}</Text>
    {failure ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{failure === "unavailable" ? t("me.loginUnavailable") : t("me.loginError")}</Text> : null}
    <Button testID="registration-register" label={t("registration.register")} busy={busy} onPress={onLogin} />
    <Button testID="registration-later" variant="secondary" label={t("registration.later")} onPress={closeSheet} />
  </>;
}

function WorkingStep() {
  const { t } = useTranslation();
  const theme = useTheme();
  return <View testID="registration-working" accessibilityLiveRegion="polite" style={{ gap: theme.space[4], alignItems: "center", paddingVertical: theme.space[4] }}>
    <ActivityIndicator color={theme.colors.accent} />
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, textAlign: "center" }]}>{t("registration.working")}</Text>
  </View>;
}

function ContentRows({ content }: { content: ProvisionalContent }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const rows: [string, number][] = [
    ["tanks", content.tanks],
    ["entries", content.careEntries],
    ["photos", content.mediaItems],
  ];
  return <View style={{ gap: theme.space[2] }}>
    {rows.map(([key, count]) => <View key={key} testID={`registration-count-${key}`} accessible accessibilityLabel={`${t(`registration.counts.${key}`)}: ${count}`}
      style={{ flexDirection: "row", justifyContent: "space-between", minHeight: 40, alignItems: "center", paddingHorizontal: theme.space[3], borderRadius: theme.radius.md, backgroundColor: theme.colors.surface2 }}>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(`registration.counts.${key}`)}</Text>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{count}</Text>
    </View>)}
  </View>;
}

function DroppedNote({ dropped }: { dropped: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  if (dropped === 0) return null;
  return <View testID="registration-dropped" accessible style={{ gap: theme.space[1] }}>
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("registration.dropped", { number: dropped })}</Text>
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("registration.droppedHelp")}</Text>
  </View>;
}

function DecisionStep({ content, dropped }: { content: ProvisionalContent; dropped: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { decide } = useRegistration();
  return <>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("registration.decision.title")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("registration.decision.text")}</Text>
    <ContentRows content={content} />
    <DroppedNote dropped={dropped} />
    <Button testID="registration-take-along" label={t("registration.decision.yes")} onPress={() => decide(true)} />
    <Button testID="registration-keep-on-device" variant="secondary" label={t("registration.decision.no")} onPress={() => decide(false)} />
  </>;
}

function DoneStep({ result, moved, dropped }: { result: "upgraded" | "moved" | "declined"; moved: ProvisionalContent; dropped: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { closeSheet } = useRegistration();
  return <>
    <Text testID="registration-done" accessibilityRole="header" accessibilityLiveRegion="polite" style={[theme.type.h1, { color: theme.colors.fg }]}>{t(`registration.done.${result}Title`)}</Text>
    <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(`registration.done.${result}Text`)}</Text>
    {result === "declined" ? null : <ContentRows content={moved} />}
    <DroppedNote dropped={dropped} />
    <Button testID="registration-close" label={t("registration.done.close")} onPress={closeSheet} />
  </>;
}

function ErrorStep({ error }: { error: unknown }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { retry, postpone } = useRegistration();
  return <>
    <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg }]}>{t("registration.title")}</Text>
    <Text testID="registration-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t(registrationErrorKey(error))}</Text>
    <Button testID="registration-retry" label={t("common.retry")} onPress={retry} />
    <Button testID="registration-postpone" variant="secondary" label={t("registration.later")} onPress={postpone} />
  </>;
}
