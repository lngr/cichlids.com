import { useCallback, useEffect, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import { ApiError, type CareEntry } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { usePagedList } from "../hooks/usePagedList";
import { reminderRefresh } from "../reminders/refreshSignal";
import { useCareActors } from "./useCareActors";

const key = (entry: CareEntry) => String(entry.id);
export function useCareStream(tankId: number, authorId: string | number) {
  const { status, accountId } = useAuth();
  const identity = `${accountId}:${authorId}`;
  const [ownerIdentity, setOwnerIdentity] = useState<string | null>(null);
  const owner = status === "authenticated" && identity === ownerIdentity;
  useEffect(() => {
    let cancelled = false;
    setOwnerIdentity(null);
    if (status === "authenticated") void apiClient.me.get().then(me => {
      if (!cancelled && String(me.profile.id) === String(authorId)) setOwnerIdentity(identity);
    }).catch(() => {});
    return () => { cancelled = true; };
  }, [status, identity, authorId]);
  const { actor, state, measureActor, measureState, attachActor, attachState } = useCareActors(() => tankId);
  const revision = state.context.revision + measureState.context.revision + attachState.context.revision;
  const loggedRevision = useRef(revision);
  useEffect(() => {
    if (loggedRevision.current === revision) return;
    loggedRevision.current = revision;
    reminderRefresh.request();
  }, [revision]);
  // Everyone reads the care stream the tank shares; a stream the owner keeps private answers care_not_found.
  const [hidden, setHidden] = useState(false);
  const fetchPage = useCallback(async (offset: number, limit: number) => {
    try {
      const page = await apiClient.care.list(tankId, { offset, limit });
      setHidden(false);
      return page;
    } catch (error) {
      if (!owner && error instanceof ApiError && error.code === "care_not_found") {
        setHidden(true);
        return { total: 0, items: [] };
      }
      throw error;
    }
  }, [owner, tankId]);
  const list = usePagedList<CareEntry>(fetchPage, [owner, identity, tankId, revision], key);
  useFocusEffect(useCallback(() => { list.reload(); }, [list.reload]));
  return { owner, tankId, state, send: actor.send, measure: { state: measureState, send: measureActor.send }, attach: { state: attachState, send: attachActor.send }, revision, list, entries: list.items, hidden };
}
export type CareStream = ReturnType<typeof useCareStream>;
