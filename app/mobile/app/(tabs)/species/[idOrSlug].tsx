import { useEffect, useState } from "react";
import { Pressable, ScrollView, StyleSheet, Text, View } from "react-native";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import { apiClient } from "../../../src/api/client";
import { useTheme } from "../../../src/theme";
import { DataRow } from "../../../src/components/DataRow";
import { ErrorView, LoadingView } from "../../../src/components/StatusView";
import type { SpeciesDetail, SpeciesLink } from "@cichlids/client-core";

const BREEDING_LABELS: Record<string, string> = {
  unspecified: "unbekannt",
  mouthbreeder: "Maulbrüter",
  cave_breeder: "Höhlenbrüter",
  substrate_breeder: "Substratlaicher",
};

const DIET_LABELS: Record<string, string> = {
  unspecified: "unbekannt",
  omnivore: "Allesfresser",
  carnivore: "Fleischfresser",
  herbivore: "Pflanzenfresser",
  limnivore: "Aufwuchsfresser",
};

export default function SpeciesDetailScreen() {
  const { idOrSlug } = useLocalSearchParams<{ idOrSlug: string }>();
  const theme = useTheme();
  const router = useRouter();
  const navigation = useNavigation();

  const [species, setSpecies] = useState<SpeciesDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    apiClient.species
      .get(idOrSlug)
      .then((detail) => {
        if (cancelled) return;
        setSpecies(detail);
        navigation.setOptions({ title: detail.displayName });
      })
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : "Fehler beim Laden"))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [idOrSlug, navigation]);

  if (loading) return <LoadingView label="Art laden…" />;
  if (error || !species) return <ErrorView message={error ?? "Art nicht gefunden"} />;

  const pictureCount = Number(species.pictureCount);

  return (
    <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={styles.content}>
      <Text style={[theme.type.h1, { color: theme.colors.fg, fontStyle: "italic" }]}>{species.displayName}</Text>
      {species.commonNames.length > 0 ? (
        <Text style={[theme.type.body, { color: theme.colors.muted, marginTop: 2 }]}>{species.commonNames.join(", ")}</Text>
      ) : null}

      {species.description ? (
        <Text style={[theme.type.body, { color: theme.colors.fg, marginTop: 12 }]}>{species.description}</Text>
      ) : null}

      <Text style={[theme.type.h2, { color: theme.colors.fg, marginTop: 20, marginBottom: 4 }]}>Pflege</Text>
      <DataRow label="Temperatur" value={species.temperatureRange} />
      <DataRow label="pH-Wert" value={species.phRange} />
      <DataRow label="GH" value={species.ghRange} />
      <DataRow label="KH" value={species.khRange} />
      <DataRow label="Endgröße" value={species.maxSize} />
      <DataRow label="Fortpflanzung" value={BREEDING_LABELS[species.breeding] ?? species.breeding} />
      <DataRow label="Ernährung" value={DIET_LABELS[species.diet] ?? species.diet} />
      <DataRow label="Herkunft" value={species.origin} />
      <DataRow label="Lebensraum" value={species.habitat} />
      <DataRow label="Formen" value={species.morphs} />

      <Pressable
        style={[styles.pictureLink, { backgroundColor: theme.colors.accentWeak, borderRadius: theme.radius.md }]}
        onPress={() => species.slug && router.push(`/gallery?species=${species.slug}`)}
        disabled={!species.slug || pictureCount === 0}
        accessibilityRole="button"
      >
        <Text style={[theme.type.bodyStrong, { color: theme.colors.accent }]}>
          {pictureCount} {pictureCount === 1 ? "Bild" : "Bilder"} in der Galerie ansehen →
        </Text>
      </Pressable>

      {species.links.length > 0 ? (
        <View style={styles.links}>
          <Text style={[theme.type.h2, { color: theme.colors.fg, marginBottom: 4 }]}>Links</Text>
          {species.links.map((link: SpeciesLink, index: number) => (
            <Text key={index} style={[theme.type.body, { color: theme.colors.accent, marginTop: 4 }]}>
              {link.label ?? link.url}
            </Text>
          ))}
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: 16, paddingBottom: 32 },
  pictureLink: {
    marginTop: 20,
    padding: 14,
    minHeight: 48,
    justifyContent: "center",
  },
  links: { marginTop: 20 },
});
