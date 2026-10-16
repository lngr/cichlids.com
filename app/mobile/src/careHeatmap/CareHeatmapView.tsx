import { useState } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { heatmapListItems, layoutHeatmap, type HeatmapCell } from "@cichlids/client-core";
import { minTouchTarget, useTheme, type Theme } from "../theme";
import { heatmapItemText, heatmapSummary } from "./heatmapText";
import { useCareHeatmap } from "./useCareHeatmap";

const CELL_GAP = 3;
const MAX_WIDTH = 480;
const DOT = 5;
/** How strongly the accent covers a cell per intensity step 1 to 4. */
const COVER = [0, 0.3, 0.5, 0.75, 1] as const;

function Swatch({ level, dot = false, theme }: { level: number; dot?: boolean; theme: Theme }) {
  const dotColor = level >= 3 ? theme.colors.onAccent : theme.colors.accent;
  return <View style={{ width: 14, height: 14, borderRadius: 3, backgroundColor: theme.colors.placeholder, overflow: "hidden", alignItems: "center", justifyContent: "center" }}>
    <View style={[StyleSheet.absoluteFill, { backgroundColor: theme.colors.accent, opacity: COVER[level] }]} />
    {dot ? <View style={{ width: DOT, height: DOT, borderRadius: DOT / 2, backgroundColor: dotColor }} /> : null}
  </View>;
}

/** A day of rest: an outlined cell with a bar and no accent cover, so it reads without color. */
function restStyle(theme: Theme) {
  return { borderRadius: 3, borderWidth: 1, borderStyle: "dashed" as const, borderColor: theme.colors.muted, alignItems: "center" as const, justifyContent: "center" as const };
}

function Cell({ cell, theme }: { cell: HeatmapCell; theme: Theme }) {
  const marked = cell.level > 0;
  if (cell.decommissioned && cell.inWindow && !marked) {
    return <View testID={`care-heatmap-rest-${cell.date}`} style={[{ flex: 1, aspectRatio: 1 }, restStyle(theme)]}>
      <View style={{ width: "50%", height: 2, backgroundColor: theme.colors.muted }} />
    </View>;
  }
  return <View testID={marked ? `care-heatmap-marked-${cell.date}` : `care-heatmap-cell-${cell.date}`}
    style={{ flex: 1, aspectRatio: 1, borderRadius: 3, overflow: "hidden", alignItems: "center", justifyContent: "center",
      backgroundColor: cell.inWindow ? theme.colors.placeholder : "transparent" }}>
    {cell.inWindow ? <View style={[StyleSheet.absoluteFill, { backgroundColor: theme.colors.accent, opacity: COVER[cell.level] }]} /> : null}
    {cell.measurements > 0 ? <View style={{ width: DOT, height: DOT, borderRadius: DOT / 2, backgroundColor: cell.level >= 3 ? theme.colors.onAccent : theme.colors.accent }} /> : null}
  </View>;
}

/**
 * The care of the last 16 weeks as a grid of week columns and weekday rows: the intensity of a day follows
 * the number of entries, a dot marks a day with a measurement. The grid reads as one summary for a screen
 * reader, and a button shows the same days as a list. Days on which a decommissioning pauses all care are outlined
 * with a bar. Absent while the heatmap is unread or not visible to
 * the viewer. Without a tank id it shows the own tanks summed. Framed puts it on a card.
 */
export function CareHeatmapView({ tankId, framed = false }: { tankId?: number; framed?: boolean }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const heatmap = useCareHeatmap(tankId);
  const [list, setList] = useState(false);
  if (!heatmap) return null;
  const layout = layoutHeatmap(heatmap);
  const items = heatmapListItems(heatmap);
  const summary = heatmapSummary(heatmap, t, i18n.language);
  const text = { color: theme.colors.muted };
  return <View testID={tankId === undefined ? "care-heatmap-all" : "care-heatmap"} style={[{ gap: theme.space[2] }, framed ? { padding: theme.space[4], borderRadius: theme.radius.lg, backgroundColor: theme.colors.surface } : null]}>
    <Text accessibilityRole="header" style={[theme.type.eyebrow, text]}>{t(tankId === undefined ? "careHeatmap.titleAll" : "careHeatmap.title")}</Text>
    <View testID="care-heatmap-grid" accessible accessibilityRole="image" accessibilityLabel={summary} importantForAccessibility="yes"
      style={{ flexDirection: "row", gap: CELL_GAP, width: "100%", maxWidth: MAX_WIDTH }}>
      {layout.columns.map((column, index) => <View key={index} style={{ flex: 1, gap: CELL_GAP }}>
        {column.map(cell => <Cell key={cell.date} cell={cell} theme={theme} />)}
      </View>)}
    </View>
    <Text testID="care-heatmap-summary" style={[theme.type.meta, text]}>{summary}</Text>
    <View accessible={false} style={{ flexDirection: "row", alignItems: "center", flexWrap: "wrap", gap: theme.space[2] }}>
      <Text style={[theme.type.meta, text]}>{t("careHeatmap.less")}</Text>
      {[0, 1, 2, 3, 4].map(level => <Swatch key={level} level={level} theme={theme} />)}
      <Text style={[theme.type.meta, text]}>{t("careHeatmap.more")}</Text>
      <Swatch level={0} dot theme={theme} />
      <Text style={[theme.type.meta, text]}>{t("careHeatmap.measurementMarker")}</Text>
      {heatmap.decommissionedDays.length > 0 ? <>
        <View testID="care-heatmap-rest-legend" style={[{ width: 14, height: 14 }, restStyle(theme)]}><View style={{ width: 7, height: 2, backgroundColor: theme.colors.muted }} /></View>
        <Text style={[theme.type.meta, text]}>{t("careHeatmap.restMarker")}</Text>
      </> : null}
    </View>
    {items.length > 0 ? <Pressable testID="care-heatmap-toggle" accessibilityRole="button" accessibilityState={{ expanded: list }}
      onPress={() => setList(value => !value)}
      style={({ pressed }) => ({ minHeight: minTouchTarget, minWidth: minTouchTarget, justifyContent: "center", alignSelf: "flex-start", opacity: pressed ? 0.7 : 1 })}>
      <Text style={[theme.type.bodyStrong, { color: theme.colors.accent }]}>{t(list ? "careHeatmap.hideList" : "careHeatmap.showList")}</Text>
    </Pressable> : null}
    {list ? <View testID="care-heatmap-list" accessibilityRole="list" style={{ gap: theme.space[1] }}>
      {items.map(item => <Text key={item.date} testID={`care-heatmap-item-${item.date}`} accessibilityRole="text"
        style={[theme.type.body, { color: theme.colors.fg, minHeight: 28 }]}>{heatmapItemText(item, t, i18n.language)}</Text>)}
    </View> : null}
  </View>;
}
