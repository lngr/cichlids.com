import { useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { toStockedInhabitant, type StockedInhabitant, type Stocking, type TankDetail } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { minTouchTarget, useTheme } from "../theme";
import { InhabitantForm } from "./InhabitantForm";
import { RemoveInhabitantSheet } from "./RemoveInhabitantSheet";
import { countText, inhabitantSummary, leftText, movedInText } from "./stockingText";

/**
 * The stocking of a tank: the current inhabitants with named fish first, an editor for the owner
 * (add a group or a named fish, change, remove with a reason) and the history of those that left as
 * a collapsible section. The inhabitants of the tank page serve until the stocking is read.
 */
export function StockingSection({ tank, stocking, owner, reload }: { tank: TankDetail; stocking: Stocking | null; owner: boolean; reload: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const tankId = Number(tank.id);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [form, setForm] = useState<{ inhabitant: StockedInhabitant | null } | null>(null);
  const [removing, setRemoving] = useState<StockedInhabitant | null>(null);
  const current = stocking?.current ?? tank.inhabitants.map(toStockedInhabitant);
  const history = stocking?.history ?? [];
  const text = { color: theme.colors.fg };
  const unknown = t("common.unknownValue");
  const labelOf = (item: StockedInhabitant) => item.name ?? item.species?.displayName ?? unknown;
  const changed = () => { setForm(null); setRemoving(null); reload(); };
  function row(item: StockedInhabitant, kind: "current" | "history") {
    const count = countText(item, i18n.language, unknown);
    const since = kind === "current" ? movedInText(item, t, i18n.language) : leftText(item, t, i18n.language);
    const speciesName = item.species?.displayName ?? null;
    const open = item.name !== null ? () => router.push(`/fish/${item.id}`)
      : item.species ? () => router.push(`/species/${item.species!.slug ?? item.species!.id}`) : undefined;
    return <View key={item.id} testID={kind === "current" ? "stocking-item" : "stocking-history-item"} accessible accessibilityLabel={inhabitantSummary(item, labelOf(item), t, i18n.language, unknown)}
      style={[styles.item, { borderColor: theme.colors.border }]}>
      <Pressable testID={`stocking-open-${item.id}`} accessibilityRole={open ? "button" : undefined} onPress={open} style={styles.itemMain} disabled={!open}>
        <Text style={[theme.type.bodyStrong, text, item.name === null ? { fontStyle: "italic" } : null]}>{labelOf(item)}</Text>
        {item.name !== null && speciesName ? <Text style={[theme.type.meta, { color: theme.colors.muted, fontStyle: "italic" }]}>{speciesName}</Text> : null}
        {since ? <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{since}</Text> : null}
      </Pressable>
      {count ? <Text style={[theme.type.bodyStrong, text]}>{count}</Text> : null}
      {owner && kind === "current" ? <View style={styles.actions}>
        <Pressable testID={`stocking-edit-${item.id}`} accessibilityRole="button" accessibilityLabel={t("stocking.editItem", { name: labelOf(item) })}
          onPress={() => setForm({ inhabitant: item })} style={styles.action}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.accent }]}>{t("stocking.edit")}</Text>
        </Pressable>
        <Pressable testID={`stocking-remove-${item.id}`} accessibilityRole="button" accessibilityLabel={t("stocking.removeItem", { name: labelOf(item) })}
          onPress={() => setRemoving(item)} style={styles.action}>
          <Text style={[theme.type.bodyStrong, { color: theme.colors.danger }]}>{t("stocking.remove")}</Text>
        </Pressable>
      </View> : null}
    </View>;
  }
  return <>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, { color: theme.colors.muted, marginBottom: theme.space[3] }]}>{t("tankStory.inhabitants")}</Text>
    {current.length === 0 ? <Text style={[theme.type.body, text]}>{t("tankStory.inhabitantsEmpty")}</Text> : null}
    {current.map(item => row(item, "current"))}
    {owner ? <View style={{ marginTop: theme.space[3] }}>
      <Button testID="stocking-add" variant="secondary" label={t("stocking.add")} onPress={() => setForm({ inhabitant: null })} />
    </View> : null}
    {history.length > 0 ? <View style={{ marginTop: theme.space[3] }}>
      <Pressable testID="stocking-history-toggle" accessibilityRole="button" accessibilityState={{ expanded: historyOpen }}
        onPress={() => setHistoryOpen(value => !value)} style={[styles.toggle, { borderColor: theme.colors.border }]}>
        <Text style={[theme.type.bodyStrong, text, { flex: 1 }]}>{t("stocking.history", { count: history.length })}</Text>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.muted }]}>{t(historyOpen ? "stocking.hide" : "stocking.show")}</Text>
      </Pressable>
      {historyOpen ? <View testID="stocking-history">{history.map(item => row(item, "history"))}</View> : null}
    </View> : null}
    {owner ? <InhabitantForm visible={form !== null} tankId={tankId} inhabitant={form?.inhabitant ?? null} onSaved={changed} onCancel={() => setForm(null)} /> : null}
    {owner ? <RemoveInhabitantSheet tankId={tankId} inhabitant={removing} label={removing ? labelOf(removing) : ""} onRemoved={changed} onCancel={() => setRemoving(null)} /> : null}
  </>;
}

const styles = StyleSheet.create({
  item: { flexDirection: "row", flexWrap: "wrap", gap: 12, minHeight: minTouchTarget, alignItems: "center", borderTopWidth: 1, paddingVertical: 8 },
  itemMain: { flex: 1, minWidth: 140, minHeight: minTouchTarget, justifyContent: "center" },
  actions: { flexDirection: "row", gap: 4, width: "100%", justifyContent: "flex-end" },
  action: { minHeight: minTouchTarget, minWidth: minTouchTarget, justifyContent: "center", paddingHorizontal: 12 },
  toggle: { flexDirection: "row", alignItems: "center", gap: 12, minHeight: minTouchTarget, borderTopWidth: 1 },
});
