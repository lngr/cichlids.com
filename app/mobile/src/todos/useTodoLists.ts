import { useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { useFocusEffect } from "expo-router";
import { createActor } from "xstate";
import { todoListMachine, type TodoListItem, type TodoState } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { usePagedList } from "../hooks/usePagedList";
import { chosenTank } from "./todoGroups";

const key = (item: TodoListItem) => String(item.id);

/**
 * The to-do list of the member: the open and the done to-dos across all tanks, the tanks a new
 * to-do can go to with the current choice, and the actor that adds, checks off, reopens and deletes
 * to-dos. Both lists load again after every change and whenever the screen gains focus.
 */
export function useTodoLists() {
  const [actor] = useState(() => createActor(todoListMachine, { input: {
    add: (tankId, text) => apiClient.todos.add(tankId, text),
    setDone: (id, done) => apiClient.todos.setDone(id, done),
    remove: id => apiClient.todos.remove(id),
  } }).start());
  useEffect(() => () => { actor.stop(); }, [actor]);
  const state = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  const revision = state.context.revision;
  const fetchState = (todoState: TodoState) => (offset: number, limit: number) => apiClient.todos.list({ state: todoState, offset, limit });
  const open = usePagedList<TodoListItem>(useCallback(fetchState("open"), []), [revision], key);
  const done = usePagedList<TodoListItem>(useCallback(fetchState("done"), []), [revision], key);
  useFocusEffect(useCallback(() => { open.reload(); done.reload(); }, [open.reload, done.reload]));

  const [tanks, setTanks] = useState<{ id: number; title: string }[]>([]);
  const [choice, setChoice] = useState<number | null>(null);
  useEffect(() => {
    let cancelled = false;
    void apiClient.me.get().then(me => apiClient.tanks.list({ user: Number(me.profile.id), limit: 50 })).then(page => {
      if (!cancelled) setTanks(page.items.map(tank => ({ id: Number(tank.id), title: tank.title })));
    }).catch(() => {});
    return () => { cancelled = true; };
  }, []);
  return { state, send: actor.send, open, done, tanks, tankId: chosenTank(tanks, choice), chooseTank: setChoice };
}
