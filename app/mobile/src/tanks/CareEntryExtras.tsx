import { useEffect, useRef, useState } from "react";
import { Modal, Pressable, ScrollView, Text, View } from "react-native";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { NOTE_MAX_LENGTH, TODO_MAX_LENGTH, isValidNote, normalizeNote, normalizeTodoText, type CareEntry } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { TextEntryModal } from "../components/TextEntryModal";
import { translateApiError } from "../lib/apiErrors";
import { minTouchTarget, useTheme } from "../theme";
import { PhotoPickButton } from "../upload/PhotoPickButton";
import { CareEntryVideos } from "../video/CareEntryVideos";
import { canAddPhoto, photoPath } from "./carePhotos";
import type { CareStream } from "./useCareStream";

type Editor = "note" | "todo" | null;

/**
 * What a care entry carries beyond its amount or values: its photos with a viewer, its note and its
 * to-dos, and the actions to add a photo, write the note and add a to-do. A sheet stays open until
 * its change has been saved.
 */
export function CareEntryExtras({ entry, stream, readOnly = false }: { entry: CareEntry; stream: CareStream; readOnly?: boolean }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const { state, send } = stream.attach;
  const [editor, setEditor] = useState<Editor>(null);
  const [viewing, setViewing] = useState<number | null>(null);
  const working = state.matches("working");
  const error = state.context.error ? translateApiError(state.context.error, t, "common.loadError") : null;
  const id = Number(entry.id);
  const revision = useRef(state.context.revision);
  useEffect(() => {
    if (state.context.revision === revision.current) return;
    revision.current = state.context.revision;
    setEditor(null);
    setViewing(null);
  }, [state.context.revision]);
  const photos = entry.photos;
  const viewed = viewing === null ? null : photos[viewing] ?? null;
  const viewedPath = viewed ? photoPath(viewed, "large") : null;
  const text = { color: theme.colors.fg };
  const closeEditor = () => setEditor(null);
  return <View style={{ gap: theme.space[2] }}>
    {photos.length > 0 ? <View testID={`care-photos-${entry.id}`} style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
      {photos.map((photo, index) => {
        const path = photoPath(photo, "thumb");
        return <Pressable key={String(photo.id)} testID={`care-photo-${photo.id}`} accessibilityRole="imagebutton"
          accessibilityLabel={t("care.photo.open", { index: index + 1, count: photos.length })} onPress={() => setViewing(index)}
          style={{ width: 72, height: 72, borderRadius: theme.radius.md, overflow: "hidden", backgroundColor: theme.colors.placeholder }}>
          {path ? <Image source={apiClient.care.photoUrl(path)} contentFit="cover" accessibilityLabel={t("care.photo.open", { index: index + 1, count: photos.length })} style={{ width: 72, height: 72 }} /> : null}
        </Pressable>;
      })}
    </View> : null}
    <CareEntryVideos entry={entry} reload={stream.list.reload} readOnly={readOnly} />
    {entry.note ? <Text testID={`care-note-text-${entry.id}`} style={[theme.type.body, text]}>{entry.note}</Text> : null}
    {readOnly ? null : entry.todos.map(todo => <View key={String(todo.id)} testID={`care-todo-${todo.id}`} accessible accessibilityLabel={t(todo.done ? "care.todo.done" : "care.todo.open", { text: todo.text })}
      style={{ flexDirection: "row", alignItems: "center", gap: theme.space[2] }}>
      <MaterialCommunityIcons name={todo.done ? "checkbox-marked-outline" : "checkbox-blank-outline"} size={20} color={theme.colors.muted} />
      <Text style={[theme.type.body, { flexShrink: 1, color: todo.done ? theme.colors.muted : theme.colors.fg, textDecorationLine: todo.done ? "line-through" : "none" }]}>{todo.text}</Text>
    </View>)}
    {readOnly ? null : <View style={{ gap: theme.space[2] }}>
      {canAddPhoto(entry)
        ? <PhotoPickButton testID={`care-photo-add-${entry.id}`} variant="secondary" label={t("care.photo.add")} disabled={working}
          onPicked={file => send({ type: "ADD_PHOTO", entryId: id, file })} onError={() => undefined} />
        : <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.photo.limit")}</Text>}
      <Button testID={`care-note-${entry.id}`} variant="secondary" label={t(entry.note ? "care.note.edit" : "care.note.add")} disabled={working} onPress={() => setEditor("note")} />
      <Button testID={`care-todo-add-${entry.id}`} variant="secondary" label={t("care.todo.add")} disabled={working} onPress={() => setEditor("todo")} />
    </View>}
    {error && editor === null && viewing === null ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <TextEntryModal visible={editor === "note"} title={t("care.note.title")} initial={entry.note ?? ""} multiline maxLength={NOTE_MAX_LENGTH} testID={`care-note-editor-${entry.id}`}
      saveLabel={t("care.note.save")} removeLabel={entry.note ? t("care.note.remove") : undefined} busy={working} error={error}
      onSave={value => isValidNote(value) && send({ type: "SAVE_NOTE", entryId: id, note: normalizeNote(value) })}
      onRemove={() => send({ type: "SAVE_NOTE", entryId: id, note: null })} onCancel={closeEditor} />
    <TextEntryModal visible={editor === "todo"} title={t("care.todo.title")} maxLength={TODO_MAX_LENGTH} testID={`care-todo-editor-${entry.id}`}
      saveLabel={t("care.todo.save")} busy={working} error={error}
      onSave={value => { const todoText = normalizeTodoText(value); if (todoText) send({ type: "ADD_TODO", entryId: id, text: todoText }); }}
      onCancel={closeEditor} />
    <Modal visible={viewed !== null} transparent animationType="fade" onRequestClose={() => setViewing(null)}>
      <View testID="care-photo-viewer" style={{ flex: 1, backgroundColor: theme.colors.bg }}>
        <ScrollView contentContainerStyle={{ flexGrow: 1, padding: theme.space[4], gap: theme.space[4], justifyContent: "center" }}>
          <Text accessibilityRole="header" style={[theme.type.h2, text]}>{t("care.photo.viewerTitle")}</Text>
          {viewedPath ? <Image testID="care-photo-viewer-image" source={apiClient.care.photoUrl(viewedPath)} contentFit="contain"
            accessibilityLabel={t("care.photo.viewerTitle")} style={{ width: "100%", aspectRatio: 4 / 3, minHeight: minTouchTarget, backgroundColor: theme.colors.placeholder }} /> : null}
          {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
          {readOnly ? null : <Button testID="care-photo-remove" variant="secondary" label={t("care.photo.remove")} busy={working}
            onPress={() => viewed && send({ type: "REMOVE_PHOTO", entryId: id, photoId: Number(viewed.id) })} />}
          <Button testID="care-photo-close" label={t("care.photo.close")} disabled={working} onPress={() => setViewing(null)} />
        </ScrollView>
      </View>
    </Modal>
  </View>;
}
