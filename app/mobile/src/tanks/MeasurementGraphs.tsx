import { useEffect, useState } from "react";
import { Modal, Pressable, ScrollView, Text, View, type LayoutChangeEvent } from "react-native";
import { useTranslation } from "react-i18next";
import {
  MEASUREMENT_PARAMETERS, PAIRING_WINDOW_MS, layoutChart, loadWaterChanges, pairWaterChanges, seriesForDisplay,
  type CareEntry, type CareParameter, type DisplaySeries, type LoadedWaterChange,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { minTouchTarget, useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { formatCareAmount } from "./careAmount";
import { chartSegments, formatEntryTime, formatMeasurementValue, waterChangePairLine } from "./measurementFormat";

const CHART_HEIGHT = 120;
const CHART_PADDING = 12;
const LINE_THICKNESS = 2;
const DOT_SIZE = 10;
const LISTED_VALUES = 10;
const HIGHLIGHT_SIZE = DOT_SIZE + 6;
const MARKER_ICON = 16;
const MARKER_LINE_TOP = 34;
const WATER_CHANGE_PAGE_SIZE = 50;
const WATER_CHANGE_MAX_PAGES = 10;

/** One graph per measured parameter of the tank, in the units of the member's system. */
export function MeasurementGraphs({ tankId, revision }: { tankId: number; revision: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const [series, setSeries] = useState<Partial<Record<CareParameter, DisplaySeries>>>({});
  const [waterChanges, setWaterChanges] = useState<LoadedWaterChange[]>([]);
  const [opened, setOpened] = useState<CareEntry | null>(null);
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const all = await Promise.all(MEASUREMENT_PARAMETERS.map(parameter => apiClient.care.series(tankId, parameter).catch(() => null)));
      const next: Partial<Record<CareParameter, DisplaySeries>> = {};
      for (const item of all) if (item && item.points.length > 0) next[item.parameter] = seriesForDisplay(item, system);
      const times = Object.values(next).flatMap(item => item.points.map(point => Date.parse(point.occurredAt)));
      const changes = times.length === 0 ? [] : await loadWaterChanges(
        (offset, limit) => apiClient.care.list(tankId, { offset, limit }), Math.min(...times) - PAIRING_WINDOW_MS,
        { pageSize: WATER_CHANGE_PAGE_SIZE, maxPages: WATER_CHANGE_MAX_PAGES },
      ).catch(() => []);
      if (cancelled) return;
      setSeries(next);
      setWaterChanges(changes);
    })();
    return () => { cancelled = true; };
  }, [tankId, revision, system]);
  const shown = MEASUREMENT_PARAMETERS.filter(parameter => series[parameter]);
  if (shown.length === 0) return null;
  return <View testID="care-graphs" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("care.measure.chartTitle")}</Text>
    {shown.map(parameter => <ParameterGraph key={parameter} series={series[parameter]!} waterChanges={waterChanges} onOpen={setOpened} />)}
    <Modal visible={opened !== null} transparent animationType="slide" onRequestClose={() => setOpened(null)}>
      <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
        <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[4] }}>
          {opened ? <WaterChangeDetail entry={opened} onClose={() => setOpened(null)} /> : null}
        </ScrollView>
      </View>
    </Modal>
  </View>;
}

function ParameterGraph({ series, waterChanges, onOpen }: { series: DisplaySeries; waterChanges: LoadedWaterChange[]; onOpen: (entry: CareEntry) => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [width, setWidth] = useState(0);
  const name = t(`care.measure.parameters.${series.parameter}`);
  const format = (value: number) => formatMeasurementValue(value, series.unit, i18n.language);
  const values = series.points.map(point => point.value);
  const latest = values[values.length - 1];
  const inner = Math.max(width - 2 * CHART_PADDING, 0);
  const layout = layoutChart(series.points, waterChanges, { width: inner, height: CHART_HEIGHT - 2 * CHART_PADDING, minMarkerGap: minTouchTarget });
  const scaled = layout.points;
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" });
  const listed = series.points.slice(-LISTED_VALUES).reverse();
  const pairs = pairWaterChanges(series, waterChanges).flatMap(pair => {
    const line = waterChangePairLine(pair, name, i18n.language);
    return line ? [{ pair, line }] : [];
  }).slice(-LISTED_VALUES);
  const highlighted = new Set(pairs.flatMap(({ pair }) => [pair.before?.entryId, pair.after?.entryId]).filter(id => id !== undefined).map(String));
  const bandHeight = layout.lanes * minTouchTarget;
  const markerStart = (x: number) => Math.min(Math.max(CHART_PADDING + x - minTouchTarget / 2, 0), Math.max(width - minTouchTarget, 0));
  return <View testID={`care-graph-${series.parameter}`} style={{ gap: theme.space[2], padding: theme.space[3], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{name}</Text>
    <View onLayout={(event: LayoutChangeEvent) => setWidth(event.nativeEvent.layout.width)} style={{ direction: "ltr" }}>
      {width > 0 ? <View importantForAccessibility="no-hide-descendants" accessibilityElementsHidden style={{ position: "absolute", top: 0, bottom: 0, start: 0, end: 0 }}>
        {layout.markers.map(marker => <View key={String(marker.id)} style={{
          position: "absolute", start: CHART_PADDING + marker.x - 1, top: marker.lane * minTouchTarget + MARKER_LINE_TOP, bottom: 0,
          width: 0, borderStartWidth: 2, borderStyle: "dashed", borderColor: theme.colors.muted,
        }} />)}
      </View> : null}
      {width > 0 && bandHeight > 0 ? <View style={{ height: bandHeight }}>
        {layout.markers.map(marker => {
          const entry = waterChanges.find(change => String(change.id) === String(marker.id))!.entry;
          return <Pressable key={String(marker.id)} testID={`care-mark-${marker.id}`} accessibilityRole="button"
            accessibilityLabel={t("care.marks.marker", { date: formatEntryTime(marker.occurredAt, i18n.language) })}
            accessibilityHint={t("care.marks.hint")} onPress={() => onOpen(entry)}
            style={{ position: "absolute", start: markerStart(marker.x), top: marker.lane * minTouchTarget, width: minTouchTarget, height: minTouchTarget }}>
            <View style={{
              position: "absolute", start: CHART_PADDING + marker.x - markerStart(marker.x) - MARKER_ICON / 2, top: (minTouchTarget - MARKER_ICON) / 2 - 4,
              width: MARKER_ICON, height: MARKER_ICON, borderRadius: MARKER_ICON / 2, borderTopStartRadius: 0, transform: [{ rotate: "-45deg" }],
              backgroundColor: theme.colors.fg, borderWidth: 2, borderColor: theme.colors.surface,
            }} />
          </Pressable>;
        })}
      </View> : null}
      <View testID={`care-graph-plot-${series.parameter}`} accessible accessibilityRole="image"
        accessibilityLabel={t("care.measure.summary", { parameter: name, latest: format(latest), count: values.length })}
        style={{ height: CHART_HEIGHT, borderBottomWidth: 1, borderStartWidth: 1, borderColor: theme.colors.border }}>
        <View importantForAccessibility="no-hide-descendants" accessibilityElementsHidden style={{ flex: 1 }}>
          {width > 0 ? chartSegments(scaled).map(segment => <View key={segment.key} style={{
            position: "absolute", start: CHART_PADDING + segment.left, top: CHART_PADDING + segment.top - LINE_THICKNESS / 2,
            width: segment.length, height: LINE_THICKNESS, borderRadius: LINE_THICKNESS / 2, backgroundColor: theme.colors.accent,
            transform: [{ rotate: `${segment.angle}rad` }],
          }} />) : null}
          {width > 0 ? scaled.map(point => {
            const marked = highlighted.has(String(point.entryId));
            const size = marked ? HIGHLIGHT_SIZE : DOT_SIZE;
            return <View key={String(point.entryId)} testID={marked ? `care-graph-highlight-${series.parameter}` : undefined} style={{
              position: "absolute", start: CHART_PADDING + point.x - size / 2, top: CHART_PADDING + point.y - size / 2,
              width: size, height: size, borderRadius: size / 2, backgroundColor: theme.colors.accent,
              borderWidth: 2, borderColor: marked ? theme.colors.fg : theme.colors.surface,
            }} />;
          }) : null}
        </View>
      </View>
    </View>
    {layout.markers.length > 0 ? <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.marks.legend")}</Text> : null}
    <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.measure.lowest", { value: format(Math.min(...values)) })}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.measure.highest", { value: format(Math.max(...values)) })}</Text>
    </View>
    {pairs.length > 0 ? <View accessibilityRole="list" accessibilityLabel={t("care.marks.listLabel")}>
      {pairs.map(({ pair, line }) => <View key={String(pair.waterChangeId)} role="listitem" style={{ minHeight: 32, justifyContent: "center" }}>
        <Text testID={`care-graph-pair-${series.parameter}`} style={[theme.type.body, { color: theme.colors.fg }]}>{line}</Text>
      </View>)}
    </View> : null}
    <View accessibilityRole="list" accessibilityLabel={t("care.measure.valuesList", { parameter: name })}>
      {listed.map(point => <View key={String(point.entryId)} role="listitem" testID={`care-graph-value-${series.parameter}`}
        style={{ flexDirection: "row", justifyContent: "space-between", minHeight: 32, alignItems: "center" }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{dateFormat.format(new Date(point.occurredAt))}</Text>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{format(point.value)}</Text>
      </View>)}
    </View>
  </View>;
}

function WaterChangeDetail({ entry, onClose }: { entry: CareEntry; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  return <View testID="care-mark-detail" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("care.waterChange")}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{formatEntryTime(entry.occurredAt, i18n.language)}</Text>
    {entry.amount ? <Text style={[theme.type.body, { color: theme.colors.fg }]}>{formatCareAmount(entry.amount, i18n.language)}</Text> : null}
    {entry.note ? <Text style={[theme.type.body, { color: theme.colors.fg }]}>{entry.note}</Text> : null}
    <Button testID="care-mark-detail-close" variant="secondary" label={t("care.marks.close")} onPress={onClose} />
  </View>;
}
