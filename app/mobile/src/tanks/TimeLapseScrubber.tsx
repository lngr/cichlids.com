import { useState } from "react";
import { useTranslation } from "react-i18next";
import { View } from "react-native";
import { useTheme } from "../theme";

export function TimeLapseScrubber({ index, count, onSeek }: { index: number; count: number; onSeek: (index: number) => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [width, setWidth] = useState(1);
  return <View accessible onLayout={event => setWidth(event.nativeEvent.layout.width)}
    onStartShouldSetResponder={() => true}
    onResponderGrant={event => onSeek(Math.round(event.nativeEvent.locationX / width * (count - 1)))}
    onResponderMove={event => onSeek(Math.round(event.nativeEvent.locationX / width * (count - 1)))} testID="tank-time-machine-scrub" accessibilityRole="adjustable" accessibilityLabel={t("tankStory.scrub")}
    accessibilityValue={{ min: 1, max: count, now: index + 1 }}
    accessibilityActions={[{ name: "increment", label: t("tankStory.next") }, { name: "decrement", label: t("tankStory.previous") }]}
    onAccessibilityAction={event => onSeek(index + (event.nativeEvent.actionName === "increment" ? 1 : -1))}
    style={{ minWidth: 48, minHeight: 48, justifyContent: "center" }}>
    <View style={{ height: 6, borderRadius: theme.radius.pill, backgroundColor: theme.colors.border }}>
      <View style={{ height: 6, width: `${100 * index / Math.max(1, count - 1)}%`, borderRadius: theme.radius.pill, backgroundColor: theme.colors.accent }} />
    </View>
  </View>;
}
