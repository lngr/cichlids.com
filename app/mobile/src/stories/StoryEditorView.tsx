import { useCallback, useEffect, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { Image } from "expo-image";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import {
  STORY_CAPTION_MAX_LENGTH, STORY_DESCRIPTION_MAX_LENGTH, STORY_TITLE_MAX_LENGTH, STORY_TOPICS, canPublishStory, isValidCaption, moveEntry,
  normalizeCaption, storySequence, type EditorItem, type EditorSubject, type PublishableStoryTopic, type StoryEditor, type StorySubjectRequest,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { minTouchTarget, useTheme } from "../theme";
import { LibraryPicker } from "./LibraryPicker";
import { SubjectPicker } from "./SubjectPicker";
import { subjectLabel } from "./storyText";

const asTopic = (value: string): PublishableStoryTopic => ((STORY_TOPICS as readonly string[]).includes(value) ? value : "cichlids") as PublishableStoryTopic;

/**
 * The editor of one story of the signed-in member: title, text and topic, the sequence of items and cards with
 * a caption per item, the cover, the references, and publishing, withdrawing and deleting. Every change goes to
 * the server at once and the screen shows what the server answers. The order changes with the move buttons.
 */
export function StoryEditorView({ storyId }: { storyId: number }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const [story, setStory] = useState<StoryEditor | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [topic, setTopic] = useState<PublishableStoryTopic>("cichlids");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [picking, setPicking] = useState<"library" | "subject" | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const adopt = useCallback((next: StoryEditor, text = true) => {
    setStory(next);
    if (text) {
      setTitle(next.title ?? "");
      setDescription(next.description ?? "");
      setTopic(asTopic(next.topic));
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    apiClient.storyEditor.get(storyId).then(next => !cancelled && adopt(next)).catch(failure => !cancelled && setLoadError(translateApiError(failure, t, "stories.loadError")));
    return () => { cancelled = true; };
  }, [storyId, attempt, adopt, t]);

  const run = async (action: () => Promise<StoryEditor>, text = false) => {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      adopt(await action(), text);
    } catch (failure) {
      setError(translateApiError(failure, t, "stories.saveError"));
    } finally {
      setBusy(false);
    }
  };

  if (loadError) return <ErrorView message={loadError} onRetry={() => setAttempt(value => value + 1)} />;
  if (!story) return <LoadingView />;

  const items = new Map(story.items.map(item => [item.id, item]));
  const subjects = new Map(story.subjects.map(subject => [subject.id, subject]));
  const sequence = storySequence(story);
  const references = story.subjects.filter(subject => subject.position === null);
  const published = story.state === "published";
  const textChanged = title.trim() !== (story.title ?? "") || description.trim() !== (story.description ?? "") || topic !== story.topic;
  const move = (index: number, offset: -1 | 1) => run(() => apiClient.storyEditor.reorder(story.id, moveEntry(sequence, index, offset)));
  const saveText = () => run(() => apiClient.storyEditor.updateText(story.id, { title: title.trim() || null, description: description.trim() || null, topic }), true);
  const publish = () => run(async () => {
    await apiClient.stories.publish(story.id, { title: title.trim(), description: description.trim() || null, topic });
    return apiClient.storyEditor.get(story.id);
  }, true);
  const remove = async () => {
    setBusy(true);
    setError(null);
    try {
      await apiClient.storyEditor.remove(story.id);
      router.back();
    } catch (failure) {
      setError(translateApiError(failure, t, "stories.saveError"));
      setBusy(false);
    }
  };
  const addSubject = (request: StorySubjectRequest) => { setPicking(null); void run(() => apiClient.storyEditor.addSubject(story.id, request)); };
  const addFile = (id: number) => { setPicking(null); void run(() => apiClient.storyEditor.addItem(story.id, id)); };
  const input = [theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }];

  return <ScrollView testID="story-editor" style={{ flex: 1, backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3], paddingBottom: theme.space[8] }} keyboardShouldPersistTaps="handled">
    <Text testID="story-state" accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(`stories.state.${story.state}`)}</Text>
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t(published ? "stories.publishedNote" : "stories.draftNote")}</Text>
    {published && story.slug ? <Button testID="story-view" variant="secondary" label={t("stories.viewInGallery")} onPress={() => router.push(`/gallery/${story.slug}`)} /> : null}

    <TextInput testID="story-title" accessibilityLabel={t("stories.titleLabel")} placeholder={t("stories.titleLabel")} placeholderTextColor={theme.colors.muted}
      value={title} onChangeText={setTitle} maxLength={STORY_TITLE_MAX_LENGTH} editable={!busy} style={[input, { minHeight: minTouchTarget }]} />
    <TextInput testID="story-description" accessibilityLabel={t("stories.descriptionLabel")} placeholder={t("stories.descriptionLabel")} placeholderTextColor={theme.colors.muted}
      value={description} onChangeText={setDescription} maxLength={STORY_DESCRIPTION_MAX_LENGTH} multiline editable={!busy} style={[input, { minHeight: 96, textAlignVertical: "top" }]} />
    <View accessibilityRole="radiogroup" style={styles.chips}>
      {STORY_TOPICS.map(value => <Chip key={value} testID={`story-topic-${value}`} label={t(`share.topic.${value}`)} active={topic === value} onPress={() => setTopic(value)} />)}
    </View>
    <Button testID="story-save-text" variant="secondary" label={t("stories.saveText")} busy={busy} disabled={!textChanged || (published && !title.trim())} onPress={() => void saveText()} />

    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg, marginTop: theme.space[3] }]}>{t("stories.sequence")}</Text>
    {sequence.length === 0 ? <Text testID="story-sequence-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("stories.sequenceEmpty")}</Text> : null}
    {sequence.map((entry, index) => {
      const common = { index, total: sequence.length, busy, onUp: () => void move(index, -1), onDown: () => void move(index, 1) };
      if (entry.kind === "item") {
        const item = items.get(entry.id);
        return item ? <ItemCard key={`item-${item.id}`} item={item} cover={story.coverItemId === item.id || (story.coverItemId === null && story.items[0]?.id === item.id)}
          chosenCover={story.coverItemId === item.id} {...common}
          onSaveCaption={caption => void run(() => apiClient.storyEditor.setCaption(story.id, item.id, caption))}
          onCover={() => void run(() => apiClient.storyEditor.setCover(story.id, story.coverItemId === item.id ? null : item.id))}
          onRemove={() => void run(() => apiClient.storyEditor.removeItem(story.id, item.id))} /> : null;
      }
      const subject = subjects.get(entry.id);
      return subject ? <SubjectCard key={`subject-${subject.id}`} subject={subject} {...common}
        onToggle={() => void run(() => apiClient.storyEditor.placeSubject(story.id, subject.id, false))}
        onRemove={() => void run(() => apiClient.storyEditor.removeSubject(story.id, subject.id))} /> : null;
    })}
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("stories.coverHint")}</Text>

    <Button testID="story-add-file" variant="secondary" label={t("stories.addFromLibrary")} disabled={busy} onPress={() => setPicking("library")} />
    <Button testID="story-add-subject" variant="secondary" label={t("stories.addSubject")} disabled={busy} onPress={() => setPicking("subject")} />

    {references.length > 0 || story.unavailableSubjectIds.length > 0 ? <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg, marginTop: theme.space[3] }]}>{t("stories.references")}</Text> : null}
    {references.map(subject => <ReferenceRow key={subject.id} subject={subject} busy={busy}
      onToggle={() => void run(() => apiClient.storyEditor.placeSubject(story.id, subject.id, true))}
      onRemove={() => void run(() => apiClient.storyEditor.removeSubject(story.id, subject.id))} />)}
    {story.unavailableSubjectIds.map(id => <View key={id} style={styles.row}>
      <Text style={[theme.type.body, { color: theme.colors.muted, flex: 1 }]}>{t("stories.unavailable")}</Text>
      <Button testID={`story-unavailable-remove-${id}`} variant="secondary" label={t("stories.remove")} disabled={busy} onPress={() => void run(() => apiClient.storyEditor.removeSubject(story.id, id))} />
    </View>)}

    {error ? <Text testID="story-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    {published
      ? <Button testID="story-withdraw" variant="secondary" label={t("stories.withdraw")} busy={busy} onPress={() => void run(() => apiClient.storyEditor.withdraw(story.id), true)} />
      : <Button testID="story-publish" label={t("stories.publish")} busy={busy} disabled={!canPublishStory(story, title)} onPress={() => void publish()} />}
    {confirmDelete
      ? <View testID="story-delete-confirm" style={{ gap: theme.space[2] }}>
        <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.fg }]}>{t(published ? "stories.deletePublishedText" : "stories.deleteDraftText")}</Text>
        <Button testID="story-delete-yes" label={t("stories.deleteYes")} busy={busy} onPress={() => void remove()} />
        <Button testID="story-delete-no" variant="secondary" label={t("stories.deleteNo")} disabled={busy} onPress={() => setConfirmDelete(false)} />
      </View>
      : <Button testID="story-delete" variant="secondary" label={t("stories.delete")} disabled={busy} onPress={() => setConfirmDelete(true)} />}

    <LibraryPicker visible={picking === "library"} taken={new Set(story.items.map(item => item.mediaItemId))} onPick={item => addFile(item.id)} onClose={() => setPicking(null)} />
    <SubjectPicker visible={picking === "subject"} onPick={addSubject} onClose={() => setPicking(null)} />
  </ScrollView>;
}

type Move = { index: number; total: number; busy: boolean; onUp: () => void; onDown: () => void };

function MoveButtons({ index, total, busy, onUp, onDown, prefix }: Move & { prefix: string }) {
  const { t } = useTranslation();
  return <View style={{ flexDirection: "row", gap: 8 }}>
    <IconAction testID={`${prefix}-up`} icon="arrow-up" label={t("stories.moveUp")} disabled={busy || index === 0} onPress={onUp} />
    <IconAction testID={`${prefix}-down`} icon="arrow-down" label={t("stories.moveDown")} disabled={busy || index === total - 1} onPress={onDown} />
  </View>;
}

function IconAction({ icon, label, onPress, disabled, testID }: { icon: "arrow-up" | "arrow-down" | "close"; label: string; onPress: () => void; disabled: boolean; testID: string }) {
  const theme = useTheme();
  return <Pressable testID={testID} accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled }} disabled={disabled} onPress={onPress}
    style={[styles.icon, { borderColor: theme.colors.border, backgroundColor: theme.colors.surface2, borderRadius: theme.radius.md, opacity: disabled ? 0.4 : 1 }]}>
    <MaterialCommunityIcons name={icon} size={22} color={theme.colors.fg} />
  </Pressable>;
}

function ItemCard({ item, cover, chosenCover, index, total, busy, onUp, onDown, onSaveCaption, onCover, onRemove }: Move & {
  item: EditorItem; cover: boolean; chosenCover: boolean; onSaveCaption: (caption: string | null) => void; onCover: () => void; onRemove: () => void;
}) {
  const theme = useTheme();
  const { t } = useTranslation();
  const [caption, setCaption] = useState(item.caption ?? "");
  useEffect(() => setCaption(item.caption ?? ""), [item.caption]);
  const picture = item.kind === "video" ? item.video?.posterUrl ?? null : item.image?.small ?? item.image?.thumb ?? null;
  const dirty = caption.trim() !== (item.caption ?? "");
  const label = t("stories.itemLabel", { kind: t(`library.kind.${item.kind}`), position: index + 1, total });
  return <View testID={`story-item-${item.id}`} accessible={false} style={[styles.card, { borderColor: cover ? theme.colors.accent : theme.colors.border, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md }]}>
    <View style={styles.row}>
      <View style={[styles.thumb, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}>
        {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" accessibilityLabel={label} /> : null}
      </View>
      <View style={{ flex: 1, gap: 4 }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{label}</Text>
        {cover ? <Text testID={`story-item-${item.id}-cover`} style={[theme.type.meta, { color: theme.colors.accent }]}>{t("stories.isCover")}</Text> : null}
      </View>
      <MoveButtons index={index} total={total} busy={busy} onUp={onUp} onDown={onDown} prefix={`story-item-${item.id}`} />
    </View>
    <TextInput testID={`story-item-${item.id}-caption`} accessibilityLabel={t("stories.captionLabel")} placeholder={t("stories.captionLabel")} placeholderTextColor={theme.colors.muted}
      value={caption} onChangeText={setCaption} maxLength={STORY_CAPTION_MAX_LENGTH} multiline editable={!busy}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.bg, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3], minHeight: minTouchTarget }]} />
    <View style={styles.actions}>
      {dirty ? <Button testID={`story-item-${item.id}-save`} variant="secondary" label={t("stories.saveCaption")} disabled={busy || !isValidCaption(caption)} onPress={() => onSaveCaption(normalizeCaption(caption))} /> : null}
      <Button testID={`story-item-${item.id}-cover-toggle`} variant="secondary" label={t(chosenCover ? "stories.clearCover" : "stories.makeCover")} disabled={busy} onPress={onCover} />
      <Button testID={`story-item-${item.id}-remove`} variant="secondary" label={t("stories.remove")} disabled={busy} onPress={onRemove} />
    </View>
  </View>;
}

function SubjectCard({ subject, index, total, busy, onUp, onDown, onToggle, onRemove }: Move & { subject: EditorSubject; onToggle: () => void; onRemove: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const label = subjectLabel(subject, t) ?? t("stories.unavailable");
  const picture = subject.tank?.image?.small ?? subject.tank?.image?.thumb ?? null;
  return <View testID={`story-subject-${subject.id}`} style={[styles.card, { borderColor: theme.colors.border, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md }]}>
    <View style={styles.row}>
      <View style={[styles.thumb, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}>
        {picture ? <Image source={picture} style={StyleSheet.absoluteFill} contentFit="cover" accessibilityLabel={label} /> : null}
      </View>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, flex: 1 }]}>{label}</Text>
      <MoveButtons index={index} total={total} busy={busy} onUp={onUp} onDown={onDown} prefix={`story-subject-${subject.id}`} />
    </View>
    <View style={styles.actions}>
      <Button testID={`story-subject-${subject.id}-reference`} variant="secondary" label={t("stories.makeReference")} disabled={busy} onPress={onToggle} />
      <Button testID={`story-subject-${subject.id}-remove`} variant="secondary" label={t("stories.remove")} disabled={busy} onPress={onRemove} />
    </View>
  </View>;
}

function ReferenceRow({ subject, busy, onToggle, onRemove }: { subject: EditorSubject; busy: boolean; onToggle: () => void; onRemove: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  return <View testID={`story-reference-${subject.id}`} style={[styles.card, { borderColor: theme.colors.border, backgroundColor: theme.colors.surface, borderRadius: theme.radius.md }]}>
    <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{subjectLabel(subject, t) ?? t("stories.unavailable")}</Text>
    <View style={styles.actions}>
      <Button testID={`story-reference-${subject.id}-card`} variant="secondary" label={t("stories.makeCard")} disabled={busy} onPress={onToggle} />
      <Button testID={`story-reference-${subject.id}-remove`} variant="secondary" label={t("stories.remove")} disabled={busy} onPress={onRemove} />
    </View>
  </View>;
}

const styles = StyleSheet.create({
  chips: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  card: { borderWidth: 1, padding: 12, gap: 8 },
  row: { flexDirection: "row", alignItems: "center", gap: 12 },
  thumb: { width: 64, height: 64, overflow: "hidden" },
  actions: { gap: 8 },
  icon: { width: minTouchTarget, height: minTouchTarget, borderWidth: 1, alignItems: "center", justifyContent: "center" },
});
