import { useEffect, useState, useSyncExternalStore } from "react";
import { createActor } from "xstate";
import { careAttachmentsMachine, careMachine, measurementMachine } from "@cichlids/client-core";
import { apiClient } from "../api/client";

/**
 * The state machines that log a water change and a measurement and that attach a note, photos and
 * to-dos to a care entry. The tank is read when an effect
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
  const [attachActor] = useState(() => createActor(careAttachmentsMachine, { input: {
    note: (id, note) => apiClient.care.setNote(tankId(), id, note),
    addPhoto: (id, file) => apiClient.care.addPhoto(tankId(), id, file),
    removePhoto: (id, photoId) => apiClient.care.removePhoto(tankId(), id, photoId),
    addTodo: (id, text) => apiClient.care.addTodo(tankId(), id, text),
  } }).start());
  useEffect(() => () => { actor.stop(); measureActor.stop(); attachActor.stop(); }, [actor, measureActor, attachActor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const measureState = useSyncExternalStore(callback => {
    const subscription = measureActor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => measureActor.getSnapshot(), () => measureActor.getSnapshot());
  const attachState = useSyncExternalStore(callback => {
    const subscription = attachActor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => attachActor.getSnapshot(), () => attachActor.getSnapshot());
  return { actor, state, measureActor, measureState, attachActor, attachState };
}
