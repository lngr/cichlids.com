import { ScrollView, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { updatePath, type RetrospectiveCard } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { formatDate } from "../lib/format";
import { followedLine } from "../today/todaySectionsText";
import { useTheme } from "../theme";
import { regionStoryPath, type RetrospectiveAction } from "./retrospectiveView";

export type ReturnCard = Extract<RetrospectiveCard, { kind: `return_${string}` }>;

/**
 * One card of the return retrospective: the welcome with the start of the absence, the overdue care,
 * the responses, the news of followed tanks and regions and the next steps with their buttons.
 */
export function ReturnCardView({ card, onAction }: { card: ReturnCard; onAction: (action: RetrospectiveAction) => void }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const title = (key: string) => <Text accessibilityRole="header" style={[theme.type.h1, styles.centered, { color: theme.colors.fg }]}>{t(key)}</Text>;
  const eyebrow = (text: string) => <Text style={[theme.type.eyebrow, styles.centered, { color: theme.colors.muted }]}>{text}</Text>;
  const meta = (text: string) => <Text style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{text}</Text>;
  const figure = (value: number, testID?: string) => (
    <Text testID={testID} style={[theme.type.display, styles.centered, { color: theme.colors.accent }]}>{new Intl.NumberFormat(i18n.language).format(value)}</Text>
  );
  const row = (testID: string, children: React.ReactNode) => (
    <View key={testID} testID={testID} style={{ gap: theme.space[1], padding: theme.space[3], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface }}>{children}</View>
  );
  const line = (text: string, strong = false) => <Text style={[strong ? theme.type.bodyStrong : theme.type.body, { color: theme.colors.fg }]}>{text}</Text>;
  const owner = (author: { displayName: string | null; username: string }) => t("today.followed.owner", { name: author.displayName || author.username });
  const untitled = t("common.untitled");
  const content = { gap: theme.space[3] };

  switch (card.kind) {
    case "return_welcome":
      return <View testID="retrospective-card-return-welcome" style={[styles.card, content]}>
        {title("retrospective.return.welcome.title")}
        <Text style={[theme.type.body, styles.centered, { color: theme.colors.muted }]}>{t("retrospective.return.welcome.text", { date: formatDate(card.since, i18n.language) })}</Text>
      </View>;
    case "return_overdue_care":
      return <ScrollView testID="retrospective-card-return-overdue" contentContainerStyle={[styles.card, content]}>
        {title("retrospective.return.overdue.title")}
        {card.items.map(item => row(`return-overdue-${item.tankId}-${item.kind}`, <>
          {line(item.tankTitle || untitled, true)}
          {line(t(`care.plan.kind.${item.kind}`))}
          {meta(t("retrospective.return.overdue.days", { days: item.daysOverdue }))}
        </>))}
      </ScrollView>;
    case "return_responses":
      return <View testID="retrospective-card-return-responses" style={[styles.card, { gap: theme.space[4] }]}>
        {title("retrospective.return.responses.title")}
        {card.kudos > 0 ? <View testID="return-kudos" style={styles.stat}>{eyebrow(t("retrospective.return.responses.kudos"))}{figure(card.kudos)}</View> : null}
        {card.comments > 0 ? <View testID="return-comments" style={styles.stat}>{eyebrow(t("retrospective.return.responses.comments"))}{figure(card.comments)}</View> : null}
      </View>;
    case "return_followed_tanks": {
      const now = new Date();
      return <ScrollView testID="retrospective-card-return-followed" contentContainerStyle={[styles.card, content]}>
        {title("retrospective.return.followed.title")}
        {card.items.map(tank => row(`return-followed-${tank.tankId}`, <>
          {line(tank.title || untitled, true)}
          {meta(owner(tank.owner))}
          {line(followedLine(tank, now, t))}
          <Button testID={`return-followed-open-${tank.tankId}`} variant="secondary" label={t("retrospective.return.followed.open")}
            accessibilityLabel={t("retrospective.return.followed.openFor", { tank: tank.title || untitled })} onPress={() => onAction({ type: "open_path", path: updatePath(tank) })} />
        </>))}
      </ScrollView>;
    }
    case "return_region_news":
      return <ScrollView testID="retrospective-card-return-region" contentContainerStyle={[styles.card, content]}>
        {title("retrospective.return.region.title")}
        {card.tanks.map(tank => row(`return-region-tank-${tank.tankId}`, <>
          {meta(t("retrospective.return.region.tank"))}
          {line(tank.title || untitled, true)}
          {meta(owner(tank.owner))}
          <Button testID={`return-region-tank-open-${tank.tankId}`} variant="secondary" label={t("retrospective.return.followed.open")}
            accessibilityLabel={t("retrospective.return.followed.openFor", { tank: tank.title || untitled })} onPress={() => onAction({ type: "open_path", path: `/tanks/${tank.tankId}` })} />
        </>))}
        {card.stories.map(story => row(`return-region-story-${story.id}`, <>
          {meta(t("retrospective.return.region.story"))}
          {line(story.title || untitled, true)}
          {meta(owner(story.author))}
          <Button testID={`return-region-story-open-${story.id}`} variant="secondary" label={t("retrospective.return.followed.open")}
            accessibilityLabel={t("retrospective.return.followed.openFor", { tank: story.title || untitled })} onPress={() => onAction({ type: "open_path", path: regionStoryPath(story) })} />
        </>))}
      </ScrollView>;
    case "return_next_steps":
      return <ScrollView testID="retrospective-card-return-next" contentContainerStyle={[styles.card, content]}>
        {title("retrospective.return.next.title")}
        {card.careTanks.map(tank => <Button key={`care-${tank.tankId}`} testID={`return-log-care-${tank.tankId}`}
          label={t("retrospective.return.next.logCare", { tank: tank.title || untitled })} onPress={() => onAction({ type: "log_care", tankId: tank.tankId })} />)}
        {card.timeLapseTanks.map(tank => <Button key={`photo-${tank.tankId}`} testID={`return-photo-${tank.tankId}`} variant="secondary"
          label={t("retrospective.return.next.photo", { tank: tank.title || untitled })} onPress={() => onAction({ type: "take_photo", tankId: tank.tankId })} />)}
      </ScrollView>;
  }
}

const styles = StyleSheet.create({
  card: {
    flexGrow: 1,
    justifyContent: "center",
    paddingHorizontal: 24,
    paddingVertical: 16,
  },
  centered: {
    textAlign: "center",
  },
  stat: {
    alignItems: "center",
  },
});
