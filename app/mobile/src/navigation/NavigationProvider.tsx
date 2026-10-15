import { createContext, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { BackHandler, Platform } from "react-native";
import { usePathname, useRouter } from "expo-router";
import * as Linking from "expo-linking";
import { createActor } from "xstate";
import { navigationMachine, type NavigationEvent } from "@cichlids/client-core";

import { bindNavigation, pathFromUrl, type NavigationAdapter } from "./adapter";

function initialPath(pathname: string): string {
  return Platform.OS === "web" ? window.location.pathname + window.location.search : pathname;
}
function createNavigation(path: string) {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "EXTERNAL", path });
  return actor;
}
const NavigationContext = createContext<ReturnType<typeof createNavigation> | null>(null);

const AdapterContext = createContext<{ current: NavigationAdapter | null }>({ current: null });
export function useNavigationAdapter() { return useContext(AdapterContext); }

export function NavigationProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [actor] = useState(() => createNavigation(initialPath(pathname)));
  const pathRef = useRef(initialPath(pathname));
  pathRef.current = initialPath(pathname);
  const adapterRef = useRef<NavigationAdapter | null>(null);
  useEffect(() => {
    const adapter = bindNavigation(actor, {
      currentPath: () => Platform.OS === "web" ? initialPath("/") : pathRef.current,
      push: path => router.push(path),
      replace: path => router.replace(path),
      dismissTo: path => router.dismissTo(path),
    });
    adapterRef.current = adapter;
    const initialRevision = actor.getSnapshot().context.revision;
    if (Platform.OS !== "web") void Linking.getInitialURL().then(url => {
      if (url && actor.getSnapshot().context.revision === initialRevision) adapter.external(pathFromUrl(url));
    });
    const back = BackHandler.addEventListener("hardwareBackPress", adapter.hardwareBack);
    const link = Linking.addEventListener("url", ({ url }) => adapter.external(pathFromUrl(url)));
    const pop = () => adapter.browserPop(initialPath("/"));
    if (Platform.OS === "web") window.addEventListener("popstate", pop, true);
    return () => {
      adapter.dispose();
      adapterRef.current = null;
      back.remove();
      link.remove();
      if (Platform.OS === "web") window.removeEventListener("popstate", pop, true);
    };
  }, [actor, router]);
  return <NavigationContext.Provider value={actor}>
    <AdapterContext.Provider value={adapterRef}>{children}</AdapterContext.Provider>
  </NavigationContext.Provider>;
}
export function useAppNavigation() {
  const actor = useContext(NavigationContext);
  if (!actor) throw new Error("NavigationProvider is required");
  const snapshot = useSyncExternalStore(callback => {
    const subscription = actor.subscribe(callback);
    return () => subscription.unsubscribe();
  }, () => actor.getSnapshot(), () => actor.getSnapshot());
  return useMemo(() => ({ snapshot, send: (event: NavigationEvent) => actor.send(event) }), [actor, snapshot]);
}
export function useMachineRouter() {
  const { send, snapshot } = useAppNavigation();
  return useMemo(() => ({
    push: (path: string) => send({ type: "OPEN", path }),
    replace: (path: string) => send({ type: "REPLACE", path }),
    dismissTo: (path: string) => send({ type: "DISMISS", path }),
    back: () => send({ type: "BACK" }),
    canGoBack: () => snapshot.context.history.length > 0,
  }), [send, snapshot.context.history.length]);
}
