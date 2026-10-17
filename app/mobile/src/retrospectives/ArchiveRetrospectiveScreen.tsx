import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import type { Retrospective } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useAppNavigation, useMachineRouter } from "../navigation/NavigationProvider";
import { RetrospectivePlayer } from "./RetrospectivePlayer";
import { ShareCardSheet } from "./ShareCardSheet";
import { routeOfAction, type RetrospectiveAction } from "./retrospectiveView";

/**
 * The archive retrospective of the member. Opening it records that the member has seen it; a member
 * without one is sent to the Me tab.
 */
export default function ArchiveRetrospectiveScreen() {
  const { status, provisional } = useAuth();
  const { t } = useTranslation();
  const router = useMachineRouter();
  const { send } = useAppNavigation();
  const [retrospective, setRetrospective] = useState<Retrospective | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [handle, setHandle] = useState<string | null>(null);
  const [sharing, setSharing] = useState(false);
  useEffect(() => {
    if (status === "anonymous" || (status === "authenticated" && provisional)) router.replace("/me");
  }, [status, provisional, router]);
  useEffect(() => {
    if (status !== "authenticated" || provisional) return;
    let cancelled = false;
    setError(null);
    void apiClient.me.archiveRetrospective().then(result => {
      if (cancelled) return;
      if (result === null) { router.replace("/me"); return; }
      setRetrospective(result);
      void apiClient.me.markArchiveRetrospectiveSeen().catch(() => {});
      void apiClient.me.get().then(me => { if (!cancelled) setHandle(me.profile.username); }).catch(() => {});
    }).catch(err => { if (!cancelled) setError(translateApiError(err, t, "common.loadError")); });
    return () => { cancelled = true; };
  // The router changes with every navigation; the retrospective loads once per attempt.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, provisional, attempt, t]);
  const open = (action: RetrospectiveAction) => {
    if (action.type === "share") setSharing(true);
    else router.push(routeOfAction(action));
  };
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!retrospective) return <LoadingView label={t("me.loading")} />;
  return <>
    <RetrospectivePlayer cards={retrospective.cards} suspended={sharing} onClose={() => send({ type: "BACK" })} onAction={open} />
    <ShareCardSheet visible={sharing} cards={retrospective.cards} handle={handle} onClose={() => setSharing(false)} />
  </>;
}
