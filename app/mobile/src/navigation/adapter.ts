import { navigationMachine } from "@cichlids/client-core";
import type { ActorRefFrom } from "xstate";

type RouterPort = {
  currentPath: () => string;
  push: (path: string) => void;
  replace: (path: string) => void;
  dismissTo: (path: string) => void;
};
export function pathFromUrl(url: string): string {
  const parsed = new URL(url);
  const path = parsed.protocol === "cichlids:" ? `/${parsed.hostname}${parsed.pathname}` : parsed.pathname.replace(/^\/--/, "");
  return (path || "/") + parsed.search;
}
export type RemoveEvent = { data: { action: { type: string } }; preventDefault: () => void };
export function bindNavigation(actor: ActorRefFrom<typeof navigationMachine>, port: RouterPort) {
  let projecting = false;
  function project() {
    const { path, mode } = actor.getSnapshot().context;
    projecting = true;
    if (mode === "push") port.push(path);
    else if (mode === "back" || mode === "dismiss") port.dismissTo(path);
    else if (path !== port.currentPath()) port.replace(path);
    queueMicrotask(() => { projecting = false; });
  }
  project();
  let revision = actor.getSnapshot().context.revision;
  const subscription = actor.subscribe(snapshot => {
    if (snapshot.context.revision === revision) return;
    revision = snapshot.context.revision;
    project();
  });
  return {
    dispose: () => subscription.unsubscribe(),
    browserPop: (path: string) => {
      if (actor.getSnapshot().matches("creating")) {
        actor.send({ type: "BACK" });
        port.replace(actor.getSnapshot().context.path);
      } else actor.send({ type: "EXTERNAL", path });
    },
    external: (path: string) => actor.send({ type: "EXTERNAL", path }),
    hardwareBack: () => {
      const snapshot = actor.getSnapshot();
      if (snapshot.matches("creating") || snapshot.context.history.length || snapshot.context.path !== "/today") {
        actor.send({ type: "BACK" });
        return true;
      }
      return false;
    },
    beforeRemove: (event: RemoveEvent) => {
      if (projecting || !["POP", "GO_BACK", "POP_TO_TOP"].includes(event.data.action.type)) return;
      event.preventDefault();
      actor.send({ type: "BACK" });
    },
  };
}
export type NavigationAdapter = ReturnType<typeof bindNavigation>;
