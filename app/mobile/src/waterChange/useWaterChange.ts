import { useEffect, useState, useSyncExternalStore } from "react";
import { createActor } from "xstate";
import { waterChangeMachine, type WaterChangeEffects } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { localStore } from "../lib/localStore";
import { reminderRefresh } from "../reminders/refreshSignal";
import { parseStoredWaterChange, waterChangeStorageKey } from "./waterChangeStorage";

/**
 * The guided water change of an own tank: the state machine with the API calls and the device
 * storage it needs.
 */
export function useWaterChange(tankId: number) {
  const [actor] = useState(() => {
    const key = waterChangeStorageKey(tankId);
    const effects: WaterChangeEffects = {
      start: () => apiClient.waterChange.start(tankId),
      markStep: (id, step, state) => apiClient.waterChange.markStep(tankId, id, step, state),
      addPhoto: (id, file, capturedInApp) => apiClient.waterChange.addPhoto(tankId, id, file, capturedInApp),
      finish: async (id, amount) => {
        const finished = await apiClient.waterChange.finish(tankId, id, amount);
        reminderRefresh.request();
        return finished;
      },
      cancel: id => apiClient.waterChange.cancel(tankId, id),
      storage: {
        load: async () => parseStoredWaterChange(await localStore.getItem(key)),
        save: value => value ? localStore.setItem(key, JSON.stringify(value)) : localStore.removeItem(key),
      },
      now: () => Date.now(),
    };
    return createActor(waterChangeMachine, { input: effects }).start();
  });
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  return { actor, state };
}
