import { useEffect, useState } from "react";
import { Modal, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import {
  checkInhabitantDraft, draftOf, inhabitantInput,
  type InhabitantDraft, type SpeciesListItem, type StockedInhabitant,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { translateApiError } from "../lib/apiErrors";
import { localCalendarDate, localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { problemKey } from "./stockingText";

const SEARCH_DELAY_MS = 250;
const SEARCH_RESULTS = 8;

const EMPTY: InhabitantDraft = { kind: "group", speciesId: null, count: "", name: "", movedInOn: "" };

/**
 * A sheet to add a group or a named fish to the stocking, or to change a current inhabitant. The
 * species comes from the catalog through a search. The moved-in day is optional and defaults to the
 * local date of the member.
 */
export function InhabitantForm({ visible, tankId, inhabitant, onSaved, onCancel }: {
  visible: boolean;
  tankId: number;
  inhabitant: StockedInhabitant | null;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const theme = useTheme();
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onCancel}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
      <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[3] }} keyboardShouldPersistTaps="handled">
        {visible ? <Form key={inhabitant?.id ?? "new"} tankId={tankId} inhabitant={inhabitant} onSaved={onSaved} onCancel={onCancel} /> : null}
      </ScrollView>
    </View>
  </Modal>;
}

function Form({ tankId, inhabitant, onSaved, onCancel }: { tankId: number; inhabitant: StockedInhabitant | null; onSaved: () => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [draft, setDraft] = useState<InhabitantDraft>(inhabitant ? draftOf(inhabitant) : EMPTY);
  const [species, setSpecies] = useState<{ id: number; name: string } | null>(inhabitant?.species ? { id: inhabitant.species.id, name: inhabitant.species.displayName } : null);
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SpeciesListItem[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);
  const editing = inhabitant !== null;
  const named = draft.kind === "named";
  useEffect(() => {
    const text = query.trim();
    if (text === "") { setResults([]); return; }
    let active = true;
    const timer = setTimeout(() => {
      void apiClient.species.list({ query: text, limit: SEARCH_RESULTS })
        .then(page => { if (active) setResults(page.items); })
        .catch(() => { if (active) setResults([]); });
    }, SEARCH_DELAY_MS);
    return () => { active = false; clearTimeout(timer); };
  }, [query]);
  const problem = checkInhabitantDraft(draft, localCalendarDate());
  const field = (invalid: boolean) => [theme.type.body, {
    color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
    borderColor: invalid ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3],
  }];
  const label = (text: string) => <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{text}</Text>;
  function choose(item: SpeciesListItem) {
    setSpecies({ id: Number(item.id), name: item.displayName });
    setDraft(value => ({ ...value, speciesId: Number(item.id) }));
    setQuery("");
    setResults([]);
  }
  async function save() {
    setTouched(true);
    if (problem !== null) return;
    setBusy(true);
    setError(null);
    try {
      const input = inhabitantInput(draft);
      if (inhabitant) await apiClient.tanks.updateInhabitant(tankId, inhabitant.id, input, localTime());
      else await apiClient.tanks.addInhabitant(tankId, input, localTime());
      onSaved();
    } catch (err) {
      setError(translateApiError(err, t, "stocking.saveError"));
    } finally {
      setBusy(false);
    }
  }
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t(editing ? "stocking.editTitle" : "stocking.addTitle")}</Text>
    {editing ? null : <View accessibilityRole="tablist" style={{ flexDirection: "row", gap: theme.space[2] }}>
      {(["group", "named"] as const).map(kind => <Pressable key={kind} testID={`stocking-kind-${kind}`} accessibilityRole="tab"
        accessibilityState={{ selected: draft.kind === kind }} onPress={() => setDraft(value => ({ ...value, kind }))}
        style={{ flex: 1, minHeight: minTouchTarget, alignItems: "center", justifyContent: "center", borderRadius: theme.radius.md, borderWidth: 1,
          borderColor: draft.kind === kind ? theme.colors.accent : theme.colors.border, backgroundColor: draft.kind === kind ? theme.colors.accentWeak : theme.colors.surface }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{t(kind === "group" ? "stocking.kindGroup" : "stocking.kindNamed")}</Text>
      </Pressable>)}
    </View>}
    {named ? <>
      {label(t("stocking.name"))}
      <TextInput testID="stocking-name" accessibilityLabel={t("stocking.name")} value={draft.name} editable={!busy}
        onChangeText={text => setDraft(value => ({ ...value, name: text }))} placeholderTextColor={theme.colors.muted} style={field(touched && (problem === "name" || problem === "name_too_long"))} />
    </> : null}
    {label(t(named ? "stocking.speciesOptional" : "stocking.species"))}
    {species ? <View style={{ flexDirection: "row", alignItems: "center", gap: theme.space[2] }}>
      <Text testID="stocking-species-selected" style={[theme.type.bodyStrong, { color: theme.colors.fg, fontStyle: "italic", flex: 1 }]}>{species.name}</Text>
      <Button testID="stocking-species-clear" variant="secondary" label={t("stocking.speciesClear")} disabled={busy}
        onPress={() => { setSpecies(null); setDraft(value => ({ ...value, speciesId: null })); }} />
    </View> : null}
    <TextInput testID="stocking-species-search" accessibilityLabel={t("stocking.speciesSearch")} placeholder={t("stocking.speciesSearch")} placeholderTextColor={theme.colors.muted}
      value={query} editable={!busy} autoCapitalize="none" autoCorrect={false} onChangeText={setQuery} style={field(touched && problem === "species")} />
    {results.map(item => <Pressable key={String(item.id)} testID={`stocking-species-option-${item.id}`} accessibilityRole="button" onPress={() => choose(item)}
      style={{ minHeight: minTouchTarget, justifyContent: "center", paddingHorizontal: theme.space[3], borderBottomWidth: 1, borderColor: theme.colors.border }}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.fg, fontStyle: "italic" }]}>{item.displayName}</Text>
    </Pressable>)}
    {query.trim() !== "" && results.length === 0 ? <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("stocking.speciesNone")}</Text> : null}
    {named ? null : <>
      {label(t("stocking.count"))}
      <TextInput testID="stocking-count" accessibilityLabel={t("stocking.count")} value={draft.count} editable={!busy} inputMode="numeric" autoCorrect={false}
        onChangeText={text => setDraft(value => ({ ...value, count: text }))} placeholderTextColor={theme.colors.muted} style={field(touched && problem === "count")} />
    </>}
    {label(t("stocking.movedIn"))}
    <TextInput testID="stocking-moved-in" accessibilityLabel={t("stocking.movedIn")} placeholder={t("stocking.dayPlaceholder")} placeholderTextColor={theme.colors.muted}
      value={draft.movedInOn} editable={!busy} inputMode="numeric" autoCapitalize="none" autoCorrect={false}
      onChangeText={text => setDraft(value => ({ ...value, movedInOn: text }))} style={field(touched && (problem === "moved_in_invalid" || problem === "moved_in_future"))} />
    {touched && problem !== null ? <Text testID="stocking-form-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{t(problemKey(problem))}</Text> : null}
    {error ? <Text testID="stocking-save-error" accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID="stocking-save" label={t("stocking.save")} busy={busy} onPress={() => void save()} />
    <Button testID="stocking-cancel" variant="secondary" label={t("navigation.cancel")} disabled={busy} onPress={onCancel} />
  </>;
}
