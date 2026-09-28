import { useEffect, useRef, useState } from "react";
import { Pressable, StyleSheet, Text, TextInput, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import type { CommentCreated, CreateCommentRequest } from "@cichlids/client-core";
import { useAuth } from "../auth/AuthProvider";
import { minTouchTarget, useTheme } from "../theme";
import { Button } from "./Button";
import { STAR_VALUES, canSubmit, emptyCommentDraft, toCreateCommentRequest, toggleStar, type CommentDraft } from "./commentComposerState";

const STAR_ICON_SIZE = 28;

/**
 * Writes a comment with an optional star rating (or a rating alone) for a picture or a tank.
 * Anonymous users see a login prompt. The draft clears after a successful post; a failed post
 * keeps the draft and offers a retry.
 */
export function CommentComposer({
  submit,
  onPosted,
}: {
  submit: (request: CreateCommentRequest) => Promise<CommentCreated>;
  onPosted: (created: CommentCreated) => void;
}) {
  const { status } = useAuth();
  if (status === "loading") return null;
  if (status === "anonymous") return <LoginPrompt />;
  return <ComposerForm submit={submit} onPosted={onPosted} />;
}

function LoginPrompt() {
  const theme = useTheme();
  const { t } = useTranslation();
  const { login, loginReady } = useAuth();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  // login() runs directly in the press handler: on the web it opens the Keycloak popup, which
  // browsers only allow as the immediate result of a user gesture.
  const onLogin = () => {
    setFailed(false);
    setBusy(true);
    login()
      .catch(() => setFailed(true))
      .finally(() => setBusy(false));
  };

  return (
    <View style={[styles.section, { gap: theme.space[2], paddingVertical: theme.space[3] }]}>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("comments.anonymousText")}</Text>
      {failed ? <Text style={[theme.type.body, { color: theme.colors.danger }]}>{t("comments.loginError")}</Text> : null}
      <Button testID="comment-login" variant="secondary" label={t("comments.login")} onPress={onLogin} disabled={!loginReady} busy={busy} />
    </View>
  );
}

function ComposerForm({
  submit,
  onPosted,
}: {
  submit: (request: CreateCommentRequest) => Promise<CommentCreated>;
  onPosted: (created: CommentCreated) => void;
}) {
  const theme = useTheme();
  const { t } = useTranslation();
  const [draft, setDraft] = useState<CommentDraft>(emptyCommentDraft);
  const [sending, setSending] = useState(false);
  const [failed, setFailed] = useState(false);
  const mountedRef = useRef(true);
  const sendingRef = useRef(false);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const send = async () => {
    if (sendingRef.current || !canSubmit(draft)) return;
    sendingRef.current = true;
    setSending(true);
    setFailed(false);
    try {
      const created = await submit(toCreateCommentRequest(draft));
      if (!mountedRef.current) return;
      setDraft(emptyCommentDraft());
      onPosted(created);
    } catch {
      if (mountedRef.current) setFailed(true);
    } finally {
      sendingRef.current = false;
      if (mountedRef.current) setSending(false);
    }
  };

  return (
    <View style={[styles.section, { gap: theme.space[3], paddingVertical: theme.space[3] }]}>
      <TextInput
        testID="comment-input"
        value={draft.body}
        onChangeText={(body) => setDraft((current) => ({ ...current, body }))}
        placeholder={t("comments.placeholder")}
        placeholderTextColor={theme.colors.muted}
        accessibilityLabel={t("comments.inputLabel")}
        multiline
        editable={!sending}
        style={[
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
        ]}
      />
      <View style={{ gap: theme.space[1] }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("comments.ratingLabel")}</Text>
        <View style={styles.starRow} accessibilityRole="radiogroup" accessibilityLabel={t("comments.ratingLabel")}>
          {STAR_VALUES.map((value) => {
            const filled = draft.stars !== null && value <= draft.stars;
            return (
              <Pressable
                key={value}
                testID={`comment-star-${value}`}
                onPress={() => setDraft((current) => ({ ...current, stars: toggleStar(current.stars, value) }))}
                disabled={sending}
                accessibilityRole="radio"
                accessibilityLabel={t("comments.star", { count: value })}
                aria-checked={draft.stars === value}
                style={styles.star}
              >
                <MaterialCommunityIcons
                  name={filled ? "star" : "star-outline"}
                  size={STAR_ICON_SIZE}
                  color={filled ? theme.colors.warn : theme.colors.muted}
                />
              </Pressable>
            );
          })}
        </View>
      </View>
      {failed ? (
        <View style={{ gap: theme.space[2] }}>
          <Text testID="comment-error" style={[theme.type.body, { color: theme.colors.danger }]}>
            {t("comments.postError")}
          </Text>
          <Button testID="comment-retry" variant="secondary" label={t("common.retry")} onPress={() => void send()} disabled={sending} />
        </View>
      ) : null}
      <Button testID="comment-submit" label={t("comments.submit")} onPress={() => void send()} disabled={!canSubmit(draft)} busy={sending} />
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    alignSelf: "stretch",
  },
  input: {
    minHeight: minTouchTarget * 2,
    borderWidth: 1,
    textAlignVertical: "top",
  },
  starRow: {
    flexDirection: "row",
  },
  star: {
    width: minTouchTarget,
    height: minTouchTarget,
    alignItems: "center",
    justifyContent: "center",
  },
});
