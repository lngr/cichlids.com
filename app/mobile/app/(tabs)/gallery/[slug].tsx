import { useMachineRouter } from "../../../src/navigation/NavigationProvider";
import { translateApiError } from "../../../src/lib/apiErrors";
import { useCallback, useEffect, useMemo, useState } from "react";
import { FlatList, Pressable, StyleSheet, Text, View } from "react-native";
import { useLocalSearchParams, useNavigation } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { listEmptyState } from "../../../src/hooks/pagedListState";
import { usePagedList } from "../../../src/hooks/usePagedList";
import { CommentComposer } from "../../../src/components/CommentComposer";
import { CommentItem } from "../../../src/components/CommentItem";
import { EmptyView, ErrorView, LoadingView } from "../../../src/components/StatusView";
import { formatDate } from "../../../src/lib/format";
import { StorySequence } from "../../../src/gallery/StorySequence";
import { SubjectCard } from "../../../src/gallery/SubjectCard";
import { buildSequence, referencesOf } from "../../../src/gallery/storySequence";
import type { Comment, CommentCreated, CreateCommentRequest, StoryDetail } from "@cichlids/client-core";

const commentKey = (comment: Comment) => comment.id;

export default function StoryDetailScreen() {
  const { slug } = useLocalSearchParams<{ slug: string }>();
  const theme = useTheme();
  const router = useMachineRouter();
  const navigation = useNavigation();
  const { t, i18n } = useTranslation();

  const [story, setStory] = useState<StoryDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    apiClient.stories
      .get(slug)
      .then((detail) => {
        if (cancelled) return;
        setStory(detail);
        navigation.setOptions({ title: detail.title ?? t("gallery.untitled") });
      })
      .catch((err) => !cancelled && setError(translateApiError(err, t, "common.loadError")))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [slug, navigation, t]);

  const fetchComments = useCallback((offset: number, limit: number) => apiClient.stories.listComments(slug, { offset, limit }), [slug]);
  const comments = usePagedList<Comment>(fetchComments, [slug], commentKey);

  const submitComment = useCallback((request: CreateCommentRequest) => apiClient.stories.createComment(slug, request), [slug]);
  // A new rating changes the story's average and count, a new comment its comment count. The
  // refetch replaces the shown story without the loading state; on failure the shown values stay.
  const onCommentPosted = useCallback(
    (created: CommentCreated) => {
      if (created.comment) comments.prepend(created.comment);
      apiClient.stories
        .get(slug)
        .then(setStory)
        .catch(() => undefined);
    },
    [slug, comments.prepend],
  );

  const emptyState = listEmptyState(comments);

  const sequence = useMemo(() => (story ? buildSequence(story) : []), [story]);
  const references = useMemo(() => (story ? referencesOf(story) : []), [story]);

  if (loading) return <LoadingView label={t("gallery.detail.loading")} />;
  if (error || !story) return <ErrorView message={error ?? t("gallery.detail.notFound")} />;

  return (
    <FlatList
      style={{ backgroundColor: theme.colors.bg }}
      data={comments.items}
      keyExtractor={(item) => String(item.id)}
      renderItem={({ item }) => <CommentItem comment={item} />}
      onEndReachedThreshold={0.4}
      onEndReached={comments.loadMore}
      ListHeaderComponent={
        <View>
          <StorySequence pages={sequence} onOpenSubject={(route) => router.push(route as never)} />
          <View style={styles.body}>
            <Text testID="picture-title" style={[theme.type.h1, { color: theme.colors.fg }]}>{story.title ?? t("gallery.untitled")}</Text>
            <Pressable onPress={() => router.push(`/profile/${story.author.id}`)}>
              <Text style={[theme.type.bodyStrong, { color: theme.colors.accent, marginTop: 4 }]}>
                {story.author.displayName ?? story.author.username}
              </Text>
            </Pressable>
            <Text testID="picture-meta" style={[theme.type.meta, { color: theme.colors.muted, marginTop: 2 }]}>
              {formatDate(story.publishedAt, i18n.language)} · {new Intl.NumberFormat(i18n.language).format(Number(story.viewCount))} {t("gallery.views", { count: Number(story.viewCount) })}
              {story.ratingAverage ? ` · ${"★".repeat(Math.round(Number(story.ratingAverage)))} (${new Intl.NumberFormat(i18n.language).format(Number(story.ratingCount))})` : ""}
            </Text>
            {story.description ? (
              <Text testID="picture-description" style={[theme.type.body, { color: theme.colors.fg, marginTop: 10 }]}>
                {story.description}
              </Text>
            ) : null}

            <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20 }]}>
              {t("gallery.detail.comments", { count: Number(story.commentCount) })}
            </Text>
            {references.length > 0 ? (
              <View testID="story-references" style={styles.references}>
                <Text style={[theme.type.eyebrow, { color: theme.colors.muted, marginBottom: 8 }]}>{t("gallery.story.references")}</Text>
                {references.map((subject) => (
                  <View key={subject.id} style={styles.reference}>
                    <SubjectCard subject={subject} onOpen={(route) => router.push(route as never)} />
                  </View>
                ))}
              </View>
            ) : null}

            <CommentComposer submit={submitComment} onPosted={onCommentPosted} ownerId={story.author.id} />
          </View>
        </View>
      }
      ListEmptyComponent={
        emptyState === "loading" ? (
          <LoadingView label={t("gallery.detail.commentsLoading")} />
        ) : emptyState === "error" ? (
          <ErrorView message={t("gallery.detail.commentsError")} onRetry={comments.reload} />
        ) : (
          <EmptyView message={t("gallery.detail.commentsEmpty")} />
        )
      }
      ListFooterComponent={comments.loadingMore ? <LoadingView /> : null}
      contentContainerStyle={styles.listContent}
    />
  );
}

const styles = StyleSheet.create({
  references: {
    marginTop: 20,
  },
  reference: {
    marginBottom: 8,
  },
  body: {
    padding: 16,
  },
  listContent: {
    paddingBottom: 32,
  },
});
