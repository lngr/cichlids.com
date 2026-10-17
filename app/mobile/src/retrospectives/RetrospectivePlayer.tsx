import { useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { AppState, Dimensions, PanResponder, Pressable, StyleSheet, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import { createActor } from "xstate";
import { RETROSPECTIVE_CARD_MS, retrospectivePlayerMachine, type RetrospectiveCard } from "@cichlids/client-core";
import { getDirection } from "../i18n";
import { minTouchTarget, useTheme } from "../theme";
import { RetrospectiveCardView } from "./RetrospectiveCardView";
import { tapDirection, type RetrospectiveAction } from "./retrospectiveView";

const SWIPE_DISTANCE = 48;

/**
 * Shows the cards of a retrospective full screen, one after the other: progress bars on top, pause and
 * close next to them, tap or swipe to move between cards and buttons for the same steps.
 */
export function RetrospectivePlayer({ cards, onClose, onAction }: {
  cards: RetrospectiveCard[];
  onClose: () => void;
  onAction: (action: RetrospectiveAction) => void;
}) {
  const theme = useTheme();
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const [actor] = useState(() => createActor(retrospectivePlayerMachine, { input: { count: cards.length } }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const playing = state.matches("playing");
  const index = state.context.index;
  const card = cards[index];
  const send = actor.send;

  useEffect(() => {
    if (!playing) return;
    const timer = setTimeout(() => send({ type: "TICK" }), RETROSPECTIVE_CARD_MS);
    return () => clearTimeout(timer);
  }, [playing, index, send]);
  useEffect(() => {
    const subscription = AppState.addEventListener("change", status => { if (status !== "active") send({ type: "PAUSE" }); });
    return () => subscription.remove();
  }, [send]);

  const swipe = useMemo(() => PanResponder.create({
    onMoveShouldSetPanResponder: (_, gesture) => Math.abs(gesture.dx) > 16 && Math.abs(gesture.dx) > Math.abs(gesture.dy) * 1.5,
    onPanResponderRelease: (_, gesture) => {
      if (Math.abs(gesture.dx) < SWIPE_DISTANCE) return;
      const forward = getDirection() ? gesture.dx > 0 : gesture.dx < 0;
      send({ type: forward ? "NEXT" : "PREV" });
    },
  }), [send]);

  if (!card) return null;
  const round = { width: minTouchTarget, height: minTouchTarget, alignItems: "center" as const, justifyContent: "center" as const };
  const icon = (name: "pause" | "play" | "close" | "chevron-left" | "chevron-right") => <MaterialCommunityIcons name={name} size={26} color={theme.colors.fg} />;
  const rtl = getDirection();
  return <View testID="retrospective-player" style={[styles.fill, { backgroundColor: theme.colors.bg, paddingTop: insets.top, paddingBottom: insets.bottom }]}>
    <View style={[styles.bars, { paddingHorizontal: theme.space[4], gap: theme.space[1] }]}>
      {cards.map((_, position) => <View key={position} testID={`retrospective-progress-${position}`}
        style={[styles.bar, { backgroundColor: position <= index ? theme.colors.accent : theme.colors.border, borderRadius: theme.radius.pill }]} />)}
    </View>
    <View style={[styles.controls, { paddingHorizontal: theme.space[2] }]}>
      <Pressable testID="retrospective-toggle" accessibilityRole="button" accessibilityLabel={t(playing ? "retrospective.pause" : "retrospective.play")}
        onPress={() => send({ type: playing ? "PAUSE" : "PLAY" })} style={round}>{icon(playing ? "pause" : "play")}</Pressable>
      <Pressable testID="retrospective-close" accessibilityRole="button" accessibilityLabel={t("retrospective.close")} onPress={onClose} style={round}>{icon("close")}</Pressable>
    </View>
    <View {...swipe.panHandlers} style={styles.fill}>
      <Pressable testID="retrospective-card" accessible={false} style={styles.fill}
        onPress={event => send({ type: tapDirection(event.nativeEvent.pageX, Dimensions.get("window").width, rtl) === "next" ? "NEXT" : "PREV" })}>
        <View style={styles.fill} accessibilityLiveRegion="polite" accessibilityLabel={t("retrospective.progress", { current: index + 1, total: cards.length })}>
          <RetrospectiveCardView card={card} onAction={onAction} />
        </View>
      </Pressable>
    </View>
    <View style={[styles.controls, { paddingHorizontal: theme.space[2] }]}>
      <Pressable testID="retrospective-previous" accessibilityRole="button" accessibilityLabel={t("retrospective.previous")} accessibilityState={{ disabled: index === 0 }}
        disabled={index === 0} onPress={() => send({ type: "PREV" })} style={[round, { opacity: index === 0 ? 0.4 : 1 }]}>{icon(rtl ? "chevron-right" : "chevron-left")}</Pressable>
      <Pressable testID="retrospective-next" accessibilityRole="button" accessibilityLabel={t("retrospective.next")} accessibilityState={{ disabled: index === cards.length - 1 }}
        disabled={index === cards.length - 1} onPress={() => send({ type: "NEXT" })} style={[round, { opacity: index === cards.length - 1 ? 0.4 : 1 }]}>{icon(rtl ? "chevron-left" : "chevron-right")}</Pressable>
    </View>
  </View>;
}

const styles = StyleSheet.create({
  fill: {
    flex: 1,
  },
  bars: {
    flexDirection: "row",
    paddingTop: 8,
  },
  bar: {
    flex: 1,
    height: 4,
  },
  controls: {
    flexDirection: "row",
    justifyContent: "space-between",
  },
});
