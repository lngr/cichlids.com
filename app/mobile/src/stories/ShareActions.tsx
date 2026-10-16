import { useEffect, useState } from "react";
import { ActivityIndicator, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { STORY_TITLE_MAX_LENGTH, STORY_TOPICS, type LibraryItem, type MyStory, type PublishableStoryTopic } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { translateApiError } from "../lib/apiErrors";
import { localTime } from "../today/localDate";
import { useRegistrationGate } from "../registration/RegistrationProvider";
import { useTheme } from "../theme";

type Panel = "share" | "story" | null;

/**
 * The gallery actions of one own photo or video: share it to the gallery once (the file then shows a link to
 * that story) and add it to any number of stories. The component renders nothing for a file the member does not
 * own. The panels open inline, so the component works inside a sheet without a second sheet on top.
 */
export function ShareActions({ mediaItemId, onNavigate }: { mediaItemId: number; onNavigate?: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const gate = useRegistrationGate();
  const [item, setItem] = useState<LibraryItem | null>(null);
  const [panel, setPanel] = useState<Panel>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setItem(null);
    apiClient.library.get(mediaItemId, localTime()).then(detail => !cancelled && setItem(detail.item)).catch(() => undefined);
    return () => { cancelled = true; };
  }, [mediaItemId]);

  if (!item) return null;
  const open = (path: string) => { onNavigate?.(); router.push(path); };
  const share = item.quickShare;
  return <View testID={`share-actions-${mediaItemId}`} style={{ gap: theme.space[2] }}>
    {share
      ? <Button testID={`share-link-${mediaItemId}`} variant="secondary" label={t(share.slug ? "share.inGallery" : "share.inDraft")}
        onPress={() => open(share.slug ? `/gallery/${share.slug}` : `/stories/${share.storyId}`)} />
      : <Button testID={`share-open-${mediaItemId}`} variant="secondary" label={t("share.action")} onPress={() => { if (gate("publish")) return; setMessage(null); setPanel(panel === "share" ? null : "share"); }} />}
    <Button testID={`share-add-story-${mediaItemId}`} variant="secondary" label={t("share.addToStory")} onPress={() => { if (gate("story")) return; setMessage(null); setPanel(panel === "story" ? null : "story"); }} />
    {message ? <Text testID={`share-message-${mediaItemId}`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.fg }]}>{message}</Text> : null}
    {panel === "share" ? <ShareForm mediaItemId={mediaItemId} onCancel={() => setPanel(null)}
      onShared={quickShare => { setItem({ ...item, quickShare }); setPanel(null); }} /> : null}
    {panel === "story" ? <StoryPicker mediaItemId={mediaItemId} onCancel={() => setPanel(null)}
      onAdded={() => { setPanel(null); setMessage(t("share.added")); }} onOpen={open} /> : null}
  </View>;
}

function ShareForm({ mediaItemId, onShared, onCancel }: {
  mediaItemId: number; onShared: (share: NonNullable<LibraryItem["quickShare"]>) => void; onCancel: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [title, setTitle] = useState("");
  const [topic, setTopic] = useState<PublishableStoryTopic>("cichlids");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const valid = title.trim().length > 0 && title.trim().length <= STORY_TITLE_MAX_LENGTH;
  const publish = async () => {
    setBusy(true);
    setError(null);
    try {
      const story = await apiClient.library.share(mediaItemId, { title: title.trim(), description: null, topic });
      onShared({ storyId: Number(story.id), slug: story.slug, state: "published" });
    } catch (failure) {
      setError(translateApiError(failure, t, "share.error"));
      setBusy(false);
    }
  };
  return <View testID="share-form" style={{ gap: theme.space[2] }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("share.formTitle")}</Text>
    <TextInput testID="share-title" accessibilityLabel={t("share.titleLabel")} placeholder={t("share.titleLabel")} placeholderTextColor={theme.colors.muted}
      value={title} onChangeText={setTitle} maxLength={STORY_TITLE_MAX_LENGTH} editable={!busy}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: 48, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, paddingHorizontal: theme.space[3] }]} />
    <View accessibilityRole="radiogroup" style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
      {STORY_TOPICS.map(value => <Chip key={value} testID={`share-topic-${value}`} label={t(`share.topic.${value}`)} active={topic === value} onPress={() => setTopic(value)} />)}
    </View>
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="share-publish" label={t("share.publish")} busy={busy} disabled={!valid} onPress={() => void publish()} />
    <Button testID="share-cancel" variant="secondary" label={t("share.cancel")} disabled={busy} onPress={onCancel} />
  </View>;
}

function StoryPicker({ mediaItemId, onAdded, onCancel, onOpen }: {
  mediaItemId: number; onAdded: () => void; onCancel: () => void; onOpen: (path: string) => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [stories, setStories] = useState<MyStory[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let cancelled = false;
    apiClient.storyEditor.list({ limit: 50 }).then(page => !cancelled && setStories(page.items))
      .catch(failure => !cancelled && (setError(translateApiError(failure, t, "share.pickError")), setStories([])));
    return () => { cancelled = true; };
  }, [t]);
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onAdded();
    } catch (failure) {
      setError(translateApiError(failure, t, "share.error"));
      setBusy(false);
    }
  };
  return <View testID="share-story-picker" style={{ gap: theme.space[2] }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("share.pickTitle")}</Text>
    {stories === null ? <ActivityIndicator color={theme.colors.accent} /> : null}
    {stories?.length === 0 && !error ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("share.pickEmpty")}</Text> : null}
    {stories?.map(story => <Button key={story.id} testID={`share-story-${story.id}`} variant="secondary" disabled={busy}
      label={`${story.title ?? t("stories.untitled")} (${t(`stories.state.${story.state}`)})`}
      onPress={() => void run(() => apiClient.storyEditor.addItem(story.id, mediaItemId))} />)}
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="share-story-new" label={t("share.newStory")} disabled={busy}
      onPress={() => void run(async () => {
        const created = await apiClient.storyEditor.create();
        await apiClient.storyEditor.addItem(created.id, mediaItemId);
        onOpen(`/stories/${created.id}`);
      })} />
    <Button testID="share-story-cancel" variant="secondary" label={t("share.cancel")} disabled={busy} onPress={onCancel} />
  </View>;
}
