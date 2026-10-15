import { useEffect, useState } from "react";
import { Text, View, type LayoutChangeEvent } from "react-native";
import { useTranslation } from "react-i18next";
import { MEASUREMENT_PARAMETERS, scaleSeries, seriesForDisplay, type CareParameter, type DisplaySeries } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { chartSegments, formatMeasurementValue } from "./measurementFormat";

const CHART_HEIGHT = 120;
const CHART_PADDING = 12;
const LINE_THICKNESS = 2;
const DOT_SIZE = 10;
const LISTED_VALUES = 10;

/** One graph per measured parameter of the tank, in the units of the member's system. */
export function MeasurementGraphs({ tankId, revision }: { tankId: number; revision: number }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const [series, setSeries] = useState<Partial<Record<CareParameter, DisplaySeries>>>({});
  useEffect(() => {
    let cancelled = false;
    void Promise.all(MEASUREMENT_PARAMETERS.map(parameter => apiClient.care.series(tankId, parameter).catch(() => null))).then(all => {
      if (cancelled) return;
      const next: Partial<Record<CareParameter, DisplaySeries>> = {};
      for (const item of all) if (item && item.points.length > 0) next[item.parameter] = seriesForDisplay(item, system);
      setSeries(next);
    });
    return () => { cancelled = true; };
  }, [tankId, revision, system]);
  const shown = MEASUREMENT_PARAMETERS.filter(parameter => series[parameter]);
  if (shown.length === 0) return null;
  return <View testID="care-graphs" style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("care.measure.chartTitle")}</Text>
    {shown.map(parameter => <ParameterGraph key={parameter} series={series[parameter]!} />)}
  </View>;
}

function ParameterGraph({ series }: { series: DisplaySeries }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const [width, setWidth] = useState(0);
  const name = t(`care.measure.parameters.${series.parameter}`);
  const format = (value: number) => formatMeasurementValue(value, series.unit, i18n.language);
  const values = series.points.map(point => point.value);
  const latest = values[values.length - 1];
  const inner = Math.max(width - 2 * CHART_PADDING, 0);
  const scaled = scaleSeries(series.points, inner, CHART_HEIGHT - 2 * CHART_PADDING);
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: "medium", timeStyle: "short" });
  const listed = series.points.slice(-LISTED_VALUES).reverse();
  return <View testID={`care-graph-${series.parameter}`} style={{ gap: theme.space[2], padding: theme.space[3], borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, backgroundColor: theme.colors.surface }}>
    <Text accessibilityRole="header" style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{name}</Text>
    <View testID={`care-graph-plot-${series.parameter}`} accessible accessibilityRole="image"
      accessibilityLabel={t("care.measure.summary", { parameter: name, latest: format(latest), count: values.length })}
      onLayout={(event: LayoutChangeEvent) => setWidth(event.nativeEvent.layout.width)}
      style={{ height: CHART_HEIGHT, direction: "ltr", borderBottomWidth: 1, borderStartWidth: 1, borderColor: theme.colors.border }}>
      <View importantForAccessibility="no-hide-descendants" accessibilityElementsHidden style={{ flex: 1 }}>
        {width > 0 ? chartSegments(scaled).map(segment => <View key={segment.key} style={{
          position: "absolute", start: CHART_PADDING + segment.left, top: CHART_PADDING + segment.top - LINE_THICKNESS / 2,
          width: segment.length, height: LINE_THICKNESS, borderRadius: LINE_THICKNESS / 2, backgroundColor: theme.colors.accent,
          transform: [{ rotate: `${segment.angle}rad` }],
        }} />) : null}
        {width > 0 ? scaled.map(point => <View key={String(point.entryId)} style={{
          position: "absolute", start: CHART_PADDING + point.x - DOT_SIZE / 2, top: CHART_PADDING + point.y - DOT_SIZE / 2,
          width: DOT_SIZE, height: DOT_SIZE, borderRadius: DOT_SIZE / 2, backgroundColor: theme.colors.accent,
          borderWidth: 2, borderColor: theme.colors.surface,
        }} />) : null}
      </View>
    </View>
    <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.measure.lowest", { value: format(Math.min(...values)) })}</Text>
      <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("care.measure.highest", { value: format(Math.max(...values)) })}</Text>
    </View>
    <View accessibilityRole="list" accessibilityLabel={t("care.measure.valuesList", { parameter: name })}>
      {listed.map(point => <View key={String(point.entryId)} role="listitem" testID={`care-graph-value-${series.parameter}`}
        style={{ flexDirection: "row", justifyContent: "space-between", minHeight: 32, alignItems: "center" }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{dateFormat.format(new Date(point.occurredAt))}</Text>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{format(point.value)}</Text>
      </View>)}
    </View>
  </View>;
}
