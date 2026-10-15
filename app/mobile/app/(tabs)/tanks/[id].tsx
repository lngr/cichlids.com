import { useAuth } from "../../../src/auth/AuthProvider";
import { translateApiError } from "../../../src/lib/apiErrors";
import { useCallback, useEffect, useState } from "react";
import { ScrollView, StyleSheet, Text, View } from "react-native";
import { useLocalSearchParams, useNavigation } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { listEmptyState } from "../../../src/hooks/pagedListState";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { CommentComposer } from "../../../src/components/CommentComposer";
import { CommentItem } from "../../../src/components/CommentItem";
import { Button } from "../../../src/components/Button";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import { TankExperience } from "../../../src/tanks/TankExperience";
import type { Comment, CommentCreated, CreateCommentRequest, TankDetail, TankTimeLapse } from "@cichlids/client-core";

const commentKey = (comment: Comment) => comment.id;

export default function TankDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const tankId = Number(id);
  const { accountId, status } = useAuth();
  const theme = useTheme();
  const navigation = useNavigation();
  const { t } = useTranslation();
  const [detail, setDetail] = useState<{ tank: TankDetail; series: TankTimeLapse } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([apiClient.tanks.get(tankId), apiClient.tanks.timeLapse(tankId)])
      .then(([tank, series]) => {
        if (cancelled) return;
        setDetail({ tank, series });
        navigation.setOptions({ title: tank.title });
      })
      .catch(err => !cancelled && setError(translateApiError(err, t, "common.loadError")))
      .finally(() => !cancelled && setLoading(false));
    return () => { cancelled = true; };
  }, [tankId, navigation, t, attempt]);
  const fetchComments = useCallback((offset: number, limit: number) => apiClient.tanks.listComments(tankId, { offset, limit }), [tankId]);
  const comments = usePagedList<Comment>(fetchComments, [tankId], commentKey);
  const submitComment = useCallback((request: CreateCommentRequest) => apiClient.tanks.createComment(tankId, request), [tankId]);
  const onCommentPosted = useCallback((created: CommentCreated) => {
    if (created.comment) comments.prepend(created.comment);
  }, [comments.prepend]);
  const emptyState = listEmptyState(comments);
  if (loading) return <LoadingView label={t("tanks.detail.loading")} />;
  if (error || !detail) return <ErrorView message={error ?? t("tanks.detail.notFound")} onRetry={() => setAttempt(value => value + 1)} />;
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
    <View style={styles.page}>
      <TankExperience key={`${tankId}-${attempt}-${accountId}-${status}`} tank={detail.tank} series={detail.series}>
        <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("tanks.detail.comments")}</Text>
        <CommentComposer submit={submitComment} onPosted={onCommentPosted} ownerId={detail.tank.author.id} />
        {comments.items.map(comment => <CommentItem key={String(comment.id)} comment={comment} />)}
        {emptyState === "loading" ? <LoadingView label={t("tanks.detail.commentsLoading")} />
          : emptyState === "error" ? <ErrorView message={t("tanks.detail.commentsError")} onRetry={comments.reload} />
          : comments.items.length === 0 ? <EmptyView message={t("tanks.detail.commentsEmpty")} /> : null}
        {comments.hasMore ? <Button label={t("tankStory.moreComments")} variant="secondary" busy={comments.loadingMore} onPress={comments.loadMore} /> : null}
      </TankExperience>
    </View>
  </ScrollView>;
}
const styles = StyleSheet.create({
  content: { paddingBottom: 32 },
  page: { width: "100%", maxWidth: 880, alignSelf: "center" },
});
