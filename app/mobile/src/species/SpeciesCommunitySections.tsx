import { Image } from "expo-image";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import type { SpeciesCommunity } from "@cichlids/client-core";
import { communitySections, partnerRoute, storyRoute, tankRoute } from "./communitySections";

/**
 * The community side of a species page: the best photos, the public tanks that keep the species and
 * the species found together with it in public tanks. A section without data is not rendered.
 */
export function SpeciesCommunitySections({ community, onNavigate }: { community: SpeciesCommunity; onNavigate: (route: string) => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const sections = communitySections(community);
  if (!sections.any) return null;
  const tankCount = Number(community.tankCount);

  return (
    <View testID="species-community" style={styles.container}>
      <Text style={[theme.type.h2, { color: theme.colors.fg }]}>{t("species.community.title")}</Text>

      {sections.photos ? (
        <View testID="species-community-photos" style={styles.section}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, marginBottom: 8 }]}>{t("species.community.photos")}</Text>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.strip}>
            {community.photos.map((photo) => (
              <Pressable
                key={String(photo.storyId)}
                testID="species-community-photo"
                style={styles.photo}
                onPress={() => onNavigate(storyRoute(photo))}
                accessibilityRole="button"
                accessibilityLabel={photo.title ?? t("gallery.untitledAccessibility")}
              >
                <Image
                  source={photo.image.small ?? photo.image.thumb ?? undefined}
                  style={[styles.photoImage, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.md }]}
                  contentFit="cover"
                />
                <Text numberOfLines={1} style={[theme.type.meta, { color: theme.colors.fg, marginTop: 4 }]}>
                  {photo.title ?? t("gallery.untitled")}
                </Text>
              </Pressable>
            ))}
          </ScrollView>
        </View>
      ) : null}

      {sections.tanks ? (
        <View testID="species-community-tanks" style={styles.section}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t("species.community.tanks")}</Text>
          <Text testID="species-community-tank-count" style={[theme.type.body, { color: theme.colors.muted, marginBottom: 8 }]}>
            {t("species.community.tankCount", { count: tankCount })}
          </Text>
          {community.tanks.map((tank) => (
            <Pressable
              key={String(tank.id)}
              testID="species-community-tank"
              style={[styles.tank, { backgroundColor: theme.colors.surface, borderColor: theme.colors.border, borderRadius: theme.radius.md }]}
              onPress={() => onNavigate(tankRoute(tank))}
              accessibilityRole="button"
              accessibilityLabel={tank.title}
            >
              <Image
                source={tank.image?.thumb ?? tank.image?.small ?? undefined}
                style={[styles.tankImage, { backgroundColor: theme.colors.placeholder, borderRadius: theme.radius.sm }]}
                contentFit="cover"
              />
              <View style={styles.tankText}>
                <Text numberOfLines={1} style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{tank.title}</Text>
                {tank.category ? <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t(`tanks.categories.${tank.category}`)}</Text> : null}
              </View>
            </Pressable>
          ))}
        </View>
      ) : null}

      {sections.partners ? (
        <View testID="species-community-partners" style={styles.section}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, marginBottom: 4 }]}>{t("species.community.partners")}</Text>
          {community.stockingPartners.map((partner) => (
            <Pressable
              key={String(partner.species.id)}
              testID="species-community-partner"
              style={[styles.partner, { borderColor: theme.colors.border }]}
              onPress={() => onNavigate(partnerRoute(partner.species))}
              accessibilityRole="button"
            >
              <Text style={[theme.type.bodyStrong, { color: theme.colors.accent, fontStyle: "italic", flexShrink: 1 }]}>
                {partner.species.displayName}
              </Text>
              <Text style={[theme.type.meta, { color: theme.colors.muted }]}>
                {t("species.community.partnerTanks", { count: Number(partner.tankCount) })}
              </Text>
            </Pressable>
          ))}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { marginTop: 24 },
  section: { marginTop: 16 },
  strip: { gap: 12 },
  photo: { width: 160, minHeight: 48 },
  photoImage: { width: 160, height: 120 },
  tank: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    padding: 8,
    minHeight: 56,
    borderWidth: 1,
    marginBottom: 8,
  },
  tankImage: { width: 56, height: 40 },
  tankText: { flex: 1 },
  partner: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    minHeight: 48,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
});
