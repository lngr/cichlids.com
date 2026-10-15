import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { useFocusEffect } from "expo-router";
import { createActor } from "xstate";
import { careMachine, type CareEntry } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { usePagedList } from "../hooks/usePagedList";

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
  const [actor] = useState(() => createActor(careMachine, { input: {
    log: () => apiClient.care.log(tankId),
    update: (id, amount) => apiClient.care.update(tankId, id, amount),
    remove: id => apiClient.care.remove(tankId, id),
  } }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const fetchPage = useCallback((offset: number, limit: number) => owner
    ? apiClient.care.list(tankId, { offset, limit }) : Promise.resolve({ total: 0, items: [] }), [owner, tankId]);
  const list = usePagedList<CareEntry>(fetchPage, [owner, identity, tankId, state.context.revision], key);
  useFocusEffect(useCallback(() => { list.reload(); }, [list.reload]));
  return { owner, state, send: actor.send, list, entries: owner ? list.items : [] };
}
export type CareStream = ReturnType<typeof useCareStream>;
