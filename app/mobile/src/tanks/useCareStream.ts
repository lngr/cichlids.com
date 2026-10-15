import { useCallback, useEffect, useState } from "react";
import { useFocusEffect } from "expo-router";
import type { CareEntry } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { usePagedList } from "../hooks/usePagedList";
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
  const { actor, state, measureActor, measureState } = useCareActors(() => tankId);
  const revision = state.context.revision + measureState.context.revision;
  const fetchPage = useCallback((offset: number, limit: number) => owner
    ? apiClient.care.list(tankId, { offset, limit }) : Promise.resolve({ total: 0, items: [] }), [owner, tankId]);
  const list = usePagedList<CareEntry>(fetchPage, [owner, identity, tankId, revision], key);
  useFocusEffect(useCallback(() => { list.reload(); }, [list.reload]));
  return { owner, tankId, state, send: actor.send, measure: { state: measureState, send: measureActor.send }, revision, list, entries: owner ? list.items : [] };
}
export type CareStream = ReturnType<typeof useCareStream>;
