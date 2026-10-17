import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { returnCards, type RetrospectiveCard } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useAppNavigation, useMachineRouter } from "../navigation/NavigationProvider";
import { localTime } from "../today/localDate";
import { RetrospectivePlayer } from "./RetrospectivePlayer";
import { routeOfAction, type RetrospectiveAction } from "./retrospectiveView";

/**
 * The return retrospective of the member in the presentation of the archive retrospective. A retrospective
 * that is not yet seen is recorded as seen when it opens; a member without one is sent to the Me tab.
 */
export default function ReturnRetrospectiveScreen() {
  const { status, provisional } = useAuth();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const { send } = useAppNavigation();
  const [cards, setCards] = useState<RetrospectiveCard[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (status === "anonymous" || (status === "authenticated" && provisional)) router.replace("/me");
  }, [status, provisional, router]);
  useEffect(() => {
    if (status !== "authenticated" || provisional) return;
    let cancelled = false;
    setError(null);
    void apiClient.me.returnRetrospective(localTime()).then(result => {
      if (cancelled) return;
      const shown = result ? returnCards(result) : [];
      if (!result || shown.length === 0) { router.replace("/me"); return; }
      setCards(shown);
      if (!result.seen) void apiClient.me.markReturnRetrospectiveSeen().catch(() => {});
    }).catch(err => { if (!cancelled) setError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  // The router changes with every navigation; the retrospective loads once per attempt.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, provisional, attempt, t]);
  // The return retrospective offers no sharing, so every action of its cards leads to a route.
  const open = (action: RetrospectiveAction) => { if (action.type !== "share") router.push(routeOfAction(action)); };
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!cards) return <LoadingView label={t("me.loading")} />;
  return <RetrospectivePlayer cards={cards} onClose={() => send({ type: "BACK" })} onAction={open} />;
}
