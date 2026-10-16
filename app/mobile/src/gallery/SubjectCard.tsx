import { Pressable, StyleSheet, Text, View } from "react-native";
import { Image } from "expo-image";
import { useTranslation } from "react-i18next";
import type { StorySubject } from "@cichlids/client-core";
import { minTouchTarget, useTheme } from "../theme";

/** The route a subject card opens. */
export function subjectRoute(subject: StorySubject): string | null {
  if (subject.tank) return `/tanks/${subject.tank.id}`;
  if (subject.inhabitant) return `/tanks/${subject.inhabitant.tankId}`;
  if (subject.species) return `/species/${subject.species.slug ?? subject.species.id}`;
  return null;
}

/**
 * A card for a tank, an inhabitant or a species a story points to. Only the subjects the viewer may
 * see arrive from the server, so every card is openable.
 */
export function SubjectCard({ subject, onOpen, fill }: { subject: StorySubject; onOpen: (route: string) => void; fill?: boolean }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const route = subjectRoute(subject);
  const tank = subject.tank;
  const inhabitant = subject.inhabitant;
  const species = subject.species;
  const label = tank ? t("gallery.story.tank") : inhabitant ? t("gallery.story.inhabitant") : t("gallery.story.species");
  const title = tank
    ? tank.title
    : inhabitant
      ? `${inhabitant.count === null ? "" : `${inhabitant.count}× `}${inhabitant.species?.displayName ?? t("gallery.story.unknownSpecies")}`
      : species?.displayName ?? "";
  const subtitle = inhabitant ? inhabitant.tankTitle : null;
  const image = tank?.image?.medium ?? tank?.image?.small ?? tank?.image?.thumb ?? null;
  return (
    <Pressable
      testID={`story-subject-${subject.type}`}
      accessibilityRole="button"
      accessibilityLabel={`${label}: ${title}`}
      onPress={() => route && onOpen(route)}
      style={[
        styles.card,
        fill && styles.fill,
        { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.lg },
      ]}
    >
      {image ? <Image source={image} contentFit="cover" style={[styles.image, { backgroundColor: theme.colors.placeholder }]} /> : null}
      <View style={styles.body}>
        <Text style={[theme.type.eyebrow, { color: theme.colors.muted }]}>{label}</Text>
        <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 2 }]}>{title}</Text>
        {subtitle ? <Text style={[theme.type.meta, { color: theme.colors.muted, marginTop: 2 }]}>{subtitle}</Text> : null}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    minHeight: minTouchTarget,
    overflow: "hidden",
    borderWidth: 1,
  },
  fill: {
    flex: 1,
    justifyContent: "center",
  },
  image: {
    width: "100%",
    aspectRatio: 16 / 9,
  },
  body: {
    padding: 12,
  },
});
