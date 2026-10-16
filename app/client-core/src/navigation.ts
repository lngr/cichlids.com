import { assign, setup } from "xstate";

export type NavigationTab = "today" | "discover" | "fish" | "me";
export type NavigationEvent =
  | { type: "TAB"; tab: NavigationTab }
  | { type: "OPEN" | "REPLACE" | "DISMISS" | "EXTERNAL"; path: string }
  | { type: "BACK" | "HOME" | "CREATE" }
  | { type: "PHOTO" | "WATER_CHANGE"; authenticated: boolean };
export type NavigationContext = {
  path: string;
  history: string[];
  revision: number;
  mode: "push" | "replace" | "dismiss" | "back" | "external";
};

export function canonicalPath(path: string): string {
  if (path === "/" || path.startsWith("/?")) return "/today";
  if (path.split("?")[0] === "/auth") return "/me";
  return path;
}
export function selectedTab(path: string): NavigationTab {
  const root = path.split(/[/?]/)[1];
  if (["discover", "gallery", "tanks", "community"].includes(root)) return "discover";
  if (["fish", "species"].includes(root)) return "fish";
  if (["me", "profile", "upload", "onboarding", "care", "todos", "media"].includes(root)) return "me";
  return "today";
}
function parentPath(path: string): string {
  const root = path.split(/[/?]/)[1];
  if (["gallery", "tanks", "community", "species"].includes(root) && path.split("?")[0] !== `/${root}`) return `/${root}`;
  const tab = selectedTab(path);
  return path.split("?")[0] === `/${tab}` ? "/today" : `/${tab}`;
}
const viewNames = ["today", "discover", "fish", "me", "gallery", "tanks", "community", "species", "profile", "upload", "onboarding", "care", "todos", "media"] as const;
export const navigationMachine = setup({
  types: { context: {} as NavigationContext, events: {} as NavigationEvent },
  actions: {
    open: assign(({ context, event }) => {
      const path = canonicalPath(event.type === "TAB" ? `/${event.tab}` : event.type === "HOME" ? "/today" : event.type === "PHOTO" || event.type === "WATER_CHANGE" ? event.authenticated ? event.type === "PHOTO" ? "/upload" : "/care" : "/me" : "path" in event ? event.path : context.path);
      const external = event.type === "EXTERNAL";
      const replace = event.type === "REPLACE" || event.type === "DISMISS" || event.type === "HOME";
      return {
        path,
        history: external || event.type === "HOME" ? [] : replace || path === context.path ? context.history : [...context.history, context.path],
        revision: context.revision + 1,
        mode: external ? "external" as const : event.type === "DISMISS" ? "dismiss" as const : replace ? "replace" as const : "push" as const,
      };
    }),
    back: assign(({ context }) => ({
      path: context.history.at(-1) ?? parentPath(context.path),
      history: context.history.slice(0, -1),
      revision: context.revision + 1,
      mode: "back" as const,
    })),
  },
}).createMachine({
  id: "navigation",
  context: { path: "/today", history: [], revision: 0, mode: "replace" },
  initial: "viewing",
  on: {
    TAB: { target: ".viewing.routing", actions: "open" },
    OPEN: { target: ".viewing.routing", actions: "open" },
    REPLACE: { target: ".viewing.routing", actions: "open" },
    DISMISS: { target: ".viewing.routing", actions: "open" },
    EXTERNAL: { target: ".viewing.routing", actions: "open" },
    HOME: { target: ".viewing.routing", actions: "open" },
  },
  states: {
    viewing: {
      initial: "routing",
      on: {
        CREATE: "#navigation.creating",
        BACK: { target: ".routing", actions: "back" },
      },
      states: {
        routing: {
          always: [
            ...viewNames.map(name => ({ guard: ({ context }: { context: NavigationContext }) => context.path.split(/[/?]/)[1] === name, target: name })),
            { target: "unknown" },
          ],
        },
        today: {}, discover: {}, fish: {}, me: {}, gallery: {}, tanks: {}, community: {}, species: {}, profile: {}, upload: {}, onboarding: {}, care: {}, todos: {}, media: {}, unknown: {},
      },
    },
    creating: {
      on: {
        BACK: "viewing.routing",
        WATER_CHANGE: { target: "viewing.routing", actions: "open" },
        PHOTO: { target: "viewing.routing", actions: "open" },
      },
    },
  },
});
