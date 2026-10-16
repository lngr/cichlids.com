import { useEffect, useState } from "react";
import { ActivityIndicator, Modal, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { StorySubjectRequest } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { filterByTitle } from "./storyText";

type Tab = "mine" | "tanks" | "species";
type Row = { id: number; label: string };

const TABS: Tab[] = ["mine", "tanks", "species"];
const PAGE = 50;

/**
 * A sheet to choose what a story points to: an own tank, any tank the member may see, or a species. The search
 * narrows the loaded tanks by title and asks the server for species. The chosen subject is added as a card in the
 * sequence or as a reference, as the switch says.
 */
export function SubjectPicker({ visible, onPick, onClose }: { visible: boolean; onPick: (request: StorySubjectRequest) => void; onClose: () => void }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [tab, setTab] = useState<Tab>("mine");
  const [query, setQuery] = useState("");
  const [positioned, setPositioned] = useState(true);
  const [rows, setRows] = useState<Row[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    let cancelled = false;
    setRows(null);
    setError(null);
    const timer = setTimeout(async () => {
      try {
        let loaded: Row[];
        if (tab === "species") {
          const page = await apiClient.species.list({ query: query.trim() || undefined, limit: PAGE });
          loaded = page.items.map(item => ({ id: Number(item.id), label: item.displayName }));
        } else {
          const own = tab === "mine" ? (await apiClient.me.get()).profile.id : undefined;
          const page = await apiClient.tanks.list({ ...(own === undefined ? {} : { user: Number(own) }), limit: PAGE });
          loaded = filterByTitle(page.items.map(item => ({ id: Number(item.id), label: item.title })), query);
        }
        if (!cancelled) setRows(loaded);
      } catch (failure) {
        if (!cancelled) { setError(translateApiError(failure, t, "stories.picker.error")); setRows([]); }
      }
    }, 250);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [visible, tab, query, t]);

  const type = tab === "species" ? "species" : "tank";
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.45)" }}>
      <View testID="subject-picker" style={{ maxHeight: "92%", width: "100%", maxWidth: 640, alignSelf: "center", backgroundColor: theme.colors.bg,
        borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet }}>
        <ScrollView contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3] }} keyboardShouldPersistTaps="handled">
          <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("stories.picker.subjectTitle")}</Text>
          <View accessibilityRole="radiogroup" style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
            {TABS.map(value => <Chip key={value} testID={`subject-tab-${value}`} label={t(`stories.picker.tab.${value}`)} active={tab === value} onPress={() => setTab(value)} />)}
          </View>
          <TextInput testID="subject-search" accessibilityLabel={t("stories.picker.search")} placeholder={t("stories.picker.search")} placeholderTextColor={theme.colors.muted}
            value={query} onChangeText={setQuery} autoCorrect={false}
            style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: 48, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, paddingHorizontal: theme.space[3] }]} />
          <View accessibilityRole="radiogroup" style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
            <Chip testID="subject-as-card" label={t("stories.picker.asCard")} active={positioned} onPress={() => setPositioned(true)} />
            <Chip testID="subject-as-reference" label={t("stories.picker.asReference")} active={!positioned} onPress={() => setPositioned(false)} />
          </View>
          {rows === null ? <ActivityIndicator color={theme.colors.accent} /> : null}
          {rows?.length === 0 && !error ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("stories.picker.none")}</Text> : null}
          {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
          {rows?.map(row => <Button key={row.id} testID={`subject-pick-${type}-${row.id}`} variant="secondary" label={row.label} onPress={() => onPick({ type, id: row.id, positioned })} />)}
          <Button testID="subject-picker-close" variant="secondary" label={t("stories.picker.close")} onPress={onClose} />
        </ScrollView>
      </View>
    </View>
  </Modal>;
}
