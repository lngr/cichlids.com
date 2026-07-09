import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { formatDate } from "../lib/format";
import type { Comment } from "@cichlids/client-core";

export function CommentItem({ comment }: { comment: Comment }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const authorName = comment.author?.displayName ?? comment.author?.username ?? comment.posterName ?? t("common.guest");
  const stars = Number(comment.score);

  return (
    <View style={[styles.container, { borderColor: theme.colors.border }]}>
      <View style={styles.headerRow}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{authorName}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{formatDate(comment.createdAt, i18n.language)}</Text>
      </View>
      {stars > 0 ? (
        <Text style={[theme.type.meta, { color: theme.colors.warn }]}>{"★".repeat(stars)}</Text>
      ) : null}
      <Text style={[theme.type.body, { color: theme.colors.fg, marginTop: 4 }]}>{comment.body}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    paddingVertical: 10,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  headerRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
});
