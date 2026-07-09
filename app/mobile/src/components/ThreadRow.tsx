import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTheme } from "../theme";
import { formatDate } from "../lib/format";
import type { CommunityThreadListItem } from "@cichlids/client-core";

export function ThreadRow({ thread, onPress }: { thread: CommunityThreadListItem; onPress: () => void }) {
  const theme = useTheme();
  const startedBy = thread.startedBy.profileRef?.displayName ?? thread.startedBy.profileRef?.username ?? thread.startedBy.posterName ?? "Gast";

  return (
    <Pressable style={[styles.row, { borderColor: theme.colors.border }]} onPress={onPress} accessibilityRole="button">
      <View style={{ flex: 1 }}>
        <Text numberOfLines={1} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{thread.title}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted, marginTop: 2 }]}>
          von {startedBy} · {formatDate(thread.createdAt)} · {Number(thread.postCount)} Antworten
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    paddingVertical: 12,
    paddingHorizontal: 16,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
