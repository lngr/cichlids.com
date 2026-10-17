import { StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import { statEntries, type RetrospectiveCard } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { formatCount } from "../lib/format";
import { useTheme } from "../theme";
import { cardImageUrl, memberSinceLabel, type RetrospectiveAction } from "./retrospectiveView";

/**
 * One card of a retrospective: a title, the figure or image it is about and, where the card leads
 * somewhere, the action to take.
 */
export function RetrospectiveCardView({ card, onAction }: { card: RetrospectiveCard; onAction: (action: RetrospectiveAction) => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const title = (key: string) => <Text accessibilityRole="header" style={[theme.type.h1, styles.centered, { color: theme.colors.fg }]}>{t(key)}</Text>;
  const eyebrow = (key: string) => <Text style={[theme.type.eyebrow, styles.centered, { color: theme.colors.muted }]}>{t(key)}</Text>;
  const figure = (text: string, testID?: string) => <Text testID={testID} style={[theme.type.display, styles.figure, { color: theme.colors.accent }]}>{text}</Text>;
  const photo = (url: string | undefined) => (
    <Image source={url} style={[styles.photo, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.lg }]} contentFit="cover" accessibilityElementsHidden />
  );

  switch (card.kind) {
    case "member_since":
      return <View testID="retrospective-card-member-since" style={[styles.card, { gap: theme.space[3] }]}>
        {title("retrospective.memberSince.title")}
        {eyebrow("retrospective.memberSince.label")}
        {figure(memberSinceLabel(card.year, card.month, i18n.language), "retrospective-member-since")}
      </View>;
    case "stats":
      return <View testID="retrospective-card-stats" style={[styles.card, { gap: theme.space[4] }]}>
        {title("retrospective.stats.title")}
        {statEntries(card).map(entry => <View key={entry.key} testID={`retrospective-stat-${entry.key}`} style={styles.stat}>
          {eyebrow(`retrospective.stats.${entry.key}`)}
          {figure(new Intl.NumberFormat(i18n.language).format(entry.value))}
        </View>)}
      </View>;
    case "most_viewed":
      return <View testID="retrospective-card-most-viewed" style={[styles.card, { gap: theme.space[3] }]}>
        {title("retrospective.mostViewed.title")}
        {photo(cardImageUrl(card.image))}
        {card.title ? <Text style={[theme.type.h2, styles.centered, { color: theme.colors.fg }]}>{card.title}</Text> : null}
        {eyebrow("retrospective.mostViewed.views")}
        {figure(formatCount(card.viewCount, i18n.language), "retrospective-view-count")}
        {card.slug ? <Button testID="retrospective-open-story" variant="secondary" label={t("retrospective.mostViewed.open")} onPress={() => onAction({ type: "open_story", slug: card.slug })} /> : null}
      </View>;
    case "archive_tank":
      return <View testID="retrospective-card-archive-tank" style={[styles.card, { gap: theme.space[3] }]}>
        {title("retrospective.archiveTank.title")}
        {photo(cardImageUrl(card.image))}
        <Text style={[theme.type.h2, styles.centered, { color: theme.colors.fg }]}>{card.title}</Text>
        <Button testID={`retrospective-adopt-${card.tankId}`} label={t("retrospective.archiveTank.adopt")} onPress={() => onAction({ type: "adopt_tank", tankId: card.tankId })} />
      </View>;
    case "tank_today":
      return <View testID="retrospective-card-tank-today" style={[styles.card, { gap: theme.space[3] }]}>
        {title("retrospective.tankToday.title")}
        <Text style={[theme.type.body, styles.centered, { color: theme.colors.muted }]}>{t("retrospective.tankToday.text")}</Text>
        <Button testID="retrospective-set-up-tank" label={t("retrospective.tankToday.action")} onPress={() => onAction({ type: "set_up_tank" })} />
      </View>;
  }
}

const styles = StyleSheet.create({
  card: {
    flex: 1,
    justifyContent: "center",
    paddingHorizontal: 24,
  },
  centered: {
    textAlign: "center",
  },
  figure: {
    textAlign: "center",
  },
  stat: {
    alignItems: "center",
  },
  photo: {
    width: "100%",
    aspectRatio: 4 / 3,
  },
});
