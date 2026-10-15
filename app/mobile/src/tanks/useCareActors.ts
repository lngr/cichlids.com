import { useEffect, useState, useSyncExternalStore } from "react";
import { createActor } from "xstate";
import { careMachine, measurementMachine } from "@cichlids/client-core";
import { apiClient } from "../api/client";

/**
 * The state machines that log a water change and a measurement. The tank is read when an effect
 * runs, so one pair of machines serves a list of tanks as well as a single one.
 */
export function useCareActors(tankId: () => number) {
  const [actor] = useState(() => createActor(careMachine, { input: {
    log: () => apiClient.care.log(tankId()),
    update: (id, amount) => apiClient.care.update(tankId(), id, amount),
    remove: id => apiClient.care.remove(tankId(), id),
  } }).start());
  const [measureActor] = useState(() => createActor(measurementMachine, { input: {
    log: values => apiClient.care.log(tankId(), { kind: "measurement", values }),
    update: (id, values) => apiClient.care.updateValues(tankId(), id, values),
    remove: id => apiClient.care.remove(tankId(), id),
  } }).start());
  useEffect(() => () => { actor.stop(); measureActor.stop(); }, [actor, measureActor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const measureState = useSyncExternalStore(callback => {
    const subscription = measureActor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => measureActor.getSnapshot(), () => measureActor.getSnapshot());
  return { actor, state, measureActor, measureState };
}
