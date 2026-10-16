import { useMachineRouter } from "../../src/navigation/NavigationProvider";
import { useEffect, useRef, useState } from "react";
import { ActivityIndicator, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import { Image } from "expo-image";
import { useLocalSearchParams } from "expo-router";
import { useTranslation } from "react-i18next";
import type { Draft, UploadFile } from "@cichlids/client-core";
import { apiClient } from "../../src/api/client";
import { useAuth } from "../../src/auth/AuthProvider";
import { NeedsRegistration } from "../../src/registration/NeedsRegistration";
import { Button } from "../../src/components/Button";
import { Chip } from "../../src/components/Chip";
import { LoadingView } from "../../src/components/StatusView";
import { publishSignal } from "../../src/lib/publishSignal";
import { minTouchTarget, useTheme } from "../../src/theme";
import { PhotoPickButton } from "../../src/upload/PhotoPickButton";
import { draftParam, draftPreviewUri, findDraft } from "../../src/upload/drafts";
import { formForResumedDraft, savedUploadInput, toSavedPhoto, type SavedUploadInput } from "../../src/upload/savedUploadInput";
import {
  TITLE_MAX_LENGTH,
  UPLOAD_TOPICS,
  canPublish,
  initialUploadForm,
  toPublishRequest,
  uploadErrorMessage,
  type UploadFormState,
} from "../../src/upload/uploadForm";

export default function UploadScreen() {
  const { status, accountId, provisional } = useAuth();
  const params = useLocalSearchParams<{ draft?: string | string[] }>();

  if (status === "loading") return <LoadingView />;
  if (status === "anonymous") return <RequireLogin />;
  if (provisional) return <NeedsRegistration reason="publish" />;
  return <UploadFlow draftId={draftParam(params.draft)} accountId={accountId} />;
}

type Phase = "loading" | "picking" | "uploading" | "editing" | "publishing" | "discarding";
type FailedAction = "load" | "missing" | "pick" | "upload" | "publish" | "discard";

/**
 * Pick a photo, upload it as a draft right away, then publish it with a title, description and
 * topic, or discard the draft. With a draft id the screen opens that unpublished draft from the
 * caller's drafts, ready to publish. A failed step shows its error with a retry of that step. The
 * draft stays on the server until it is published or discarded, also when the screen closes, so
 * the Me tab can list it. The typed input of a draft, and a picked photo whose upload has not
 * finished, stay on the device for the member until the draft is published or discarded, also
 * across a sign-out: an opened draft shows its typed input again, and the pending photo is
 * uploaded when the screen opens.
 */
function UploadFlow({ draftId, accountId }: { draftId: string | null; accountId: string | null }) {
  const theme = useTheme();
  const router = useMachineRouter();
  const { t } = useTranslation();

  const [phase, setPhase] = useState<Phase>(draftId ? "loading" : "picking");
  const [file, setFile] = useState<UploadFile | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [form, setForm] = useState<UploadFormState>(initialUploadForm);
  const [error, setError] = useState<{ message: string; action: FailedAction } | null>(null);
  const [loadAttempt, setLoadAttempt] = useState(0);
  // The input kept on the device is read once when the screen opens; until then nothing is saved.
  const [restored, setRestored] = useState(false);
  const savedRef = useRef<SavedUploadInput | null>(null);

  const mountedRef = useRef(true);
  // The draft until it is published or discarded, readable synchronously by the press handlers.
  const draftRef = useRef<Draft | null>(null);
  // The publish or discard request in flight; a second press in the same frame is ignored.
  const actionRef = useRef<"publish" | "discard" | null>(null);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    // A loaded draft keeps its form; the effect loads only while the draft is missing.
    if (!restored || !draftId || draftRef.current) return;
    let cancelled = false;
    setError(null);
    setPhase("loading");
    apiClient.me
      .drafts()
      .then((drafts) => {
        if (cancelled) return;
        const found = findDraft(drafts, draftId);
        if (!found) {
          setError({ message: t("upload.draftNotFound"), action: "missing" });
          return;
        }
        draftRef.current = found;
        setDraft(found);
        setForm(formForResumedDraft(found, savedRef.current));
        setPhase("editing");
      })
      .catch((err) => {
        if (cancelled) return;
        setError({ message: uploadErrorMessage(err, t("upload.draftLoadError"), t), action: "load" });
      });
    return () => {
      cancelled = true;
    };
  }, [restored, draftId, loadAttempt, t]);

  const upload = async (picked: UploadFile) => {
    setFile(picked);
    setError(null);
    setPhase("uploading");
    try {
      const created = await apiClient.uploads.create(picked);
      if (!mountedRef.current) return;
      draftRef.current = created;
      setDraft(created);
      setPhase("editing");
    } catch (err) {
      if (!mountedRef.current) return;
      setError({ message: uploadErrorMessage(err, t("upload.uploadError"), t), action: "upload" });
      setPhase("picking");
    }
  };

  useEffect(() => {
    let cancelled = false;
    void savedUploadInput.load(accountId).then((saved) => {
      if (cancelled) return;
      savedRef.current = saved;
      if (!draftId && saved?.photo) {
        setForm(saved.photo.form);
        void upload(saved.photo.file);
      }
      setRestored(true);
    });
    return () => {
      cancelled = true;
    };
    // Runs once when the screen opens; later input comes from the screen itself.
  }, []);

  useEffect(() => {
    if (!restored || !accountId) return;
    if (draft) {
      void savedUploadInput.rememberDraft(accountId, String(draft.id), form);
      return;
    }
    const photo = toSavedPhoto(file);
    if (photo) void savedUploadInput.rememberPhoto(accountId, photo, form);
  }, [restored, accountId, draft, file, form]);

  const publish = async () => {
    const current = draftRef.current;
    if (!current || actionRef.current || !canPublish(form)) return;
    actionRef.current = "publish";
    setError(null);
    setPhase("publishing");
    try {
      const picture = await apiClient.stories.publish(Number(current.id), toPublishRequest(form));
      draftRef.current = null;
      if (accountId) void savedUploadInput.forgetDraft(accountId, String(current.id));
      publishSignal.notifyPublished();
      if (mountedRef.current) router.dismissTo(`/gallery/${picture.slug}`);
    } catch (err) {
      if (!mountedRef.current) return;
      setError({ message: uploadErrorMessage(err, t("upload.publishError"), t), action: "publish" });
      setPhase("editing");
    } finally {
      actionRef.current = null;
    }
  };

  const discard = async () => {
    const current = draftRef.current;
    if (!current || actionRef.current) return;
    actionRef.current = "discard";
    setError(null);
    setPhase("discarding");
    try {
      await apiClient.stories.discard(Number(current.id));
      draftRef.current = null;
      if (accountId) void savedUploadInput.forgetDraft(accountId, String(current.id));
      if (!mountedRef.current) return;
      if (router.canGoBack()) router.back();
      else router.replace("/discover");
    } catch (err) {
      if (!mountedRef.current) return;
      setError({ message: uploadErrorMessage(err, t("upload.discardError"), t), action: "discard" });
      setPhase("editing");
    } finally {
      actionRef.current = null;
    }
  };

  const retry = () => {
    if (!error) return;
    if (error.action === "load") setLoadAttempt((n) => n + 1);
    else if (error.action === "upload" && file) void upload(file);
    else if (error.action === "publish") void publish();
    else if (error.action === "discard") void discard();
  };

  const previewUri = draft ? draftPreviewUri(draft) : null;
  const busy = phase === "publishing" || phase === "discarding";
  const inputStyle = [
    styles.input,
    theme.type.body,
    {
      color: theme.colors.fg,
      backgroundColor: theme.colors.surface,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
      paddingHorizontal: theme.space[3],
      paddingVertical: theme.space[2],
    },
  ];

  return (
    <ScrollView
      style={{ backgroundColor: theme.colors.bg }}
      contentContainerStyle={{ padding: theme.space[4], gap: theme.space[4] }}
      keyboardShouldPersistTaps="handled"
    >
      {draft ? (
        <View testID="upload-preview" style={[styles.preview, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}>
          {previewUri ? (
            <Image source={previewUri} style={styles.previewImage} contentFit="contain" accessibilityLabel={t("upload.previewLabel")} />
          ) : null}
        </View>
      ) : draftId ? null : (
        <View style={{ gap: theme.space[3] }}>
          <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("upload.intro")}</Text>
          <PhotoPickButton
            label={t("upload.pick")}
            disabled={phase === "uploading"}
            onPicked={(picked) => void upload(picked)}
            onError={() => {
              // After a picker error the way forward is a new pick, never a re-upload of an earlier file.
              setFile(null);
              setError({ message: t("upload.pickError"), action: "pick" });
            }}
          />
        </View>
      )}

      {phase === "loading" && !error ? (
        <View testID="upload-loading" style={[styles.busyRow, { gap: theme.space[2] }]}>
          <ActivityIndicator color={theme.colors.accent} />
          <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("upload.loadingDraft")}</Text>
        </View>
      ) : null}

      {phase === "uploading" ? (
        <View testID="upload-busy" style={[styles.busyRow, { gap: theme.space[2] }]}>
          <ActivityIndicator color={theme.colors.accent} />
          <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("upload.uploading")}</Text>
        </View>
      ) : null}

      {error ? (
        <View style={{ gap: theme.space[2] }}>
          <Text testID="upload-error" style={[theme.type.body, { color: theme.colors.danger }]}>
            {error.message}
          </Text>
          {error.action !== "pick" && error.action !== "missing" && (error.action !== "upload" || file) ? (
            <Button testID="upload-retry" variant="secondary" label={t("common.retry")} onPress={retry} disabled={busy} />
          ) : null}
        </View>
      ) : null}

      {draft ? (
        <View style={{ gap: theme.space[4] }}>
          <View style={{ gap: theme.space[1] }}>
            <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("upload.titleLabel")}</Text>
            <TextInput
              testID="upload-title"
              value={form.title}
              onChangeText={(title) => setForm((current) => ({ ...current, title }))}
              placeholder={t("upload.titlePlaceholder")}
              placeholderTextColor={theme.colors.muted}
              maxLength={TITLE_MAX_LENGTH}
              editable={!busy}
              style={inputStyle}
            />
          </View>
          <View style={{ gap: theme.space[1] }}>
            <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("upload.descriptionLabel")}</Text>
            <TextInput
              testID="upload-description"
              value={form.description}
              onChangeText={(description) => setForm((current) => ({ ...current, description }))}
              placeholder={t("upload.descriptionPlaceholder")}
              placeholderTextColor={theme.colors.muted}
              multiline
              editable={!busy}
              style={[inputStyle, styles.multiline]}
            />
          </View>
          <View style={{ gap: theme.space[2] }}>
            <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("upload.topicLabel")}</Text>
            <View style={[styles.chipRow, { gap: theme.space[2] }]}>
              {UPLOAD_TOPICS.map((topic) => (
                <Chip
                  key={topic}
                  testID={`upload-topic-${topic}`}
                  label={t(`gallery.topics.${topic}`)}
                  active={form.topic === topic}
                  onPress={() => setForm((current) => ({ ...current, topic }))}
                />
              ))}
            </View>
          </View>
          <Button
            testID="upload-publish"
            label={t("upload.publish")}
            onPress={() => void publish()}
            disabled={!canPublish(form) || phase === "discarding"}
            busy={phase === "publishing"}
          />
          <Button
            testID="upload-discard"
            variant="secondary"
            label={t("upload.discard")}
            onPress={() => void discard()}
            disabled={phase === "publishing"}
            busy={phase === "discarding"}
          />
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  preview: {
    width: "100%",
    aspectRatio: 4 / 3,
    overflow: "hidden",
  },
  previewImage: {
    width: "100%",
    height: "100%",
  },
  busyRow: {
    flexDirection: "row",
    alignItems: "center",
    minHeight: minTouchTarget,
  },
  input: {
    minHeight: minTouchTarget,
    borderWidth: 1,
  },
  multiline: {
    minHeight: minTouchTarget * 2,
    textAlignVertical: "top",
  },
  chipRow: {
    flexDirection: "row",
    flexWrap: "wrap",
  },
});

function RequireLogin() {
  const router = useMachineRouter();
  useEffect(() => { router.replace("/me"); }, [router]);
  return null;
}
