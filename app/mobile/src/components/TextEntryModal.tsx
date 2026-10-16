import { useState } from "react";
import { Modal, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { Button } from "./Button";

/**
 * A sheet with one text field and a save, an optional remove and a cancel action. The draft starts
 * from the initial text; save receives the text as typed. An error shows above the actions.
 */
export function TextEntryModal({ visible, title, initial = "", multiline = false, maxLength, saveLabel, removeLabel, busy, error, testID, onSave, onRemove, onCancel }: {
  visible: boolean;
  title: string;
  initial?: string;
  multiline?: boolean;
  maxLength: number;
  saveLabel: string;
  removeLabel?: string;
  busy: boolean;
  error: string | null;
  testID: string;
  onSave: (text: string) => void;
  onRemove?: () => void;
  onCancel: () => void;
}) {
  const theme = useTheme();
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onCancel}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
      <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[4] }} keyboardShouldPersistTaps="handled">
        {visible ? <TextEntryForm key={initial} {...{ title, initial, multiline, maxLength, saveLabel, removeLabel, busy, error, testID, onSave, onRemove, onCancel }} /> : null}
      </ScrollView>
    </View>
  </Modal>;
}

function TextEntryForm({ title, initial, multiline, maxLength, saveLabel, removeLabel, busy, error, testID, onSave, onRemove, onCancel }: {
  title: string; initial: string; multiline: boolean; maxLength: number; saveLabel: string; removeLabel?: string; busy: boolean;
  error: string | null; testID: string; onSave: (text: string) => void; onRemove?: () => void; onCancel: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [draft, setDraft] = useState(initial);
  const tooLong = draft.trim().length > maxLength;
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{title}</Text>
    <TextInput testID={`${testID}-input`} accessibilityLabel={title} value={draft} editable={!busy} multiline={multiline} autoFocus
      onChangeText={setDraft} placeholderTextColor={theme.colors.muted}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: multiline ? 120 : 48, textAlignVertical: multiline ? "top" : "center",
        borderWidth: 1, borderColor: tooLong ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
    <Text style={[theme.type.meta, { color: tooLong ? theme.colors.danger : theme.colors.muted }]}>{`${draft.trim().length} / ${maxLength}`}</Text>
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID={`${testID}-save`} label={saveLabel} busy={busy} disabled={tooLong || busy} onPress={() => onSave(draft)} />
    {onRemove && removeLabel ? <Button testID={`${testID}-remove`} variant="secondary" label={removeLabel} disabled={busy} onPress={onRemove} /> : null}
    <Button testID={`${testID}-cancel`} variant="secondary" label={t("navigation.cancel")} disabled={busy} onPress={onCancel} />
  </>;
}
