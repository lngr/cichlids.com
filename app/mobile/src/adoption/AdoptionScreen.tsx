import { useEffect, useMemo, useState } from "react";
import { ScrollView, Text, View, useWindowDimensions } from "react-native";
import { useTranslation } from "react-i18next";
import { adoptionProblemOf, toggleSuggestion, type LegacyTankDto, type PhotoSuggestion } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { listEmptyState } from "../hooks/pagedListState";
import { usePagedList } from "../hooks/usePagedList";
import { translateApiError } from "../lib/apiErrors";
import { tankSeriesSignal } from "../lib/tankSeriesSignal";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { useTheme } from "../theme";
import { SuggestionGrid } from "./SuggestionGrid";
import { adoptionProblemKey } from "./adoptionView";

type Step = "confirm" | "pick";

/**
 * The adoption of an archive tank: the member confirms the tank, picks suggested photos from the archive in
 * the order they should appear on the tank and adopts it. The tank page shows the photos afterwards.
 */
export function AdoptionScreen({ tankId }: { tankId: number }) {
  const { status, provisional } = useAuth();
  const { t } = useTranslation();
  const theme = useTheme();
  const router = useMachineRouter();
  const [tank, setTank] = useState<LegacyTankDto | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [step, setStep] = useState<Step>("confirm");
  const [chosen, setChosen] = useState<number[]>([]);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  useEffect(() => {
    if (status === "anonymous" || (status === "authenticated" && provisional)) router.replace("/me");
  // The router changes with every navigation; the guard depends on the sign-in alone.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, provisional]);
  useEffect(() => {
    if (status !== "authenticated" || provisional) return;
    let cancelled = false;
    setLoadError(null);
    apiClient.me.legacyTanks().then(tanks => {
      if (cancelled) return;
      const found = tanks.find(candidate => Number(candidate.id) === tankId);
      if (found) setTank(found); else setLoadError(t("adoption.notFound"));
    }).catch(err => { if (!cancelled) setLoadError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  }, [status, provisional, tankId, attempt, t]);
  const fetchPage = useMemo(() => (offset: number, limit: number) => apiClient.me.photoSuggestions(tankId, { offset, limit }), [tankId]);
  const suggestions = usePagedList<PhotoSuggestion>(fetchPage, [tankId], suggestion => suggestion.mediaItemId);
  const { width } = useWindowDimensions();
  const contentWidth = Math.min(width, 880) - theme.space[4] * 2;
  async function adopt() {
    setBusy(true);
    setActionError(null);
    try {
      await apiClient.me.adoptLegacyTank(tankId, chosen);
      tankSeriesSignal.notifyPublished();
      router.dismissTo(`/tanks/${tankId}`);
    } catch (err) {
      const problem = adoptionProblemOf(err);
      setActionError(problem ? t(adoptionProblemKey(problem)) : translateApiError(err, t, "common.loadError"));
      setBusy(false);
    }
  }
  if (loadError) return <ErrorView message={loadError} onRetry={() => setAttempt(value => value + 1)} />;
  if (!tank) return <LoadingView label={t("adoption.loading")} />;
  const text = { color: theme.colors.fg };
  if (step === "confirm") {
    return <ScrollView testID="adoption-confirm" style={{ backgroundColor: theme.colors.bg }}
      contentContainerStyle={{ padding: theme.space[4], gap: theme.space[4] }}>
      <Text accessibilityRole="header" style={[theme.type.h1, text]}>{t("adoption.confirmTitle")}</Text>
      <Text testID="adoption-tank-title" style={[theme.type.display, text]}>{tank.title}</Text>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("adoption.confirmText", { title: tank.title })}</Text>
      <Button testID="adoption-confirm-action" label={t("adoption.confirmAction")} onPress={() => setStep("pick")} />
    </ScrollView>;
  }
  const emptyState = listEmptyState(suggestions);
  return <View testID="adoption-pick" style={{ flex: 1, backgroundColor: theme.colors.bg }}>
    <ScrollView contentContainerStyle={{ padding: theme.space[4], gap: theme.space[3] }}>
      <Text accessibilityRole="header" style={[theme.type.h1, text]}>{t("adoption.pickTitle")}</Text>
      <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("adoption.pickText")}</Text>
      {emptyState === "loading" ? <LoadingView label={t("adoption.loading")} />
        : emptyState === "error" ? <ErrorView message={suggestions.error ?? t("common.loadError")} onRetry={suggestions.reload} />
        : suggestions.items.length === 0 ? <Text testID="adoption-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("adoption.empty")}</Text>
        : <SuggestionGrid items={suggestions.items} chosen={chosen} width={contentWidth} onToggle={id => setChosen(current => toggleSuggestion(current, id))} />}
      {suggestions.hasMore ? <Button testID="adoption-more" variant="secondary" label={t("adoption.more")} busy={suggestions.loadingMore} onPress={suggestions.loadMore} /> : null}
    </ScrollView>
    <View style={{ gap: theme.space[2], padding: theme.space[4], borderTopWidth: 1, borderColor: theme.colors.border, backgroundColor: theme.colors.surface }}>
      <Text testID="adoption-chosen" accessibilityLiveRegion="polite" style={[theme.type.bodyStrong, text]}>{t("adoption.chosen", { number: chosen.length })}</Text>
      {actionError ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{actionError}</Text> : null}
      <Button testID="adoption-adopt" label={t("adoption.adopt")} busy={busy} onPress={() => void adopt()} />
      <Button testID="adoption-back" variant="secondary" label={t("adoption.back")} disabled={busy} onPress={() => setStep("confirm")} />
    </View>
  </View>;
}
