import { assign, fromPromise, setup } from "xstate";
import type { CareAmountRequest, CareEntry } from "./client.js";

type Effects = {
  log: () => Promise<CareEntry>;
  update: (id: number, amount: CareAmountRequest | null) => Promise<CareEntry>;
  remove: (id: number) => Promise<CareEntry>;
};
export const careMachine = setup({
  types: {
    input: {} as Effects,
    context: {} as { effects: Effects; entry: CareEntry | null; amount: CareAmountRequest | null; error: unknown; revision: number },
    events: {} as { type: "LOG" | "UNDO" | "CANCEL" | "DELETE" }
      | { type: "EDIT"; entry: CareEntry } | { type: "SAVE"; amount: CareAmountRequest | null },
  },
  actors: {
    log: fromPromise(({ input }: { input: Effects }) => input.log()),
    update: fromPromise(({ input }: { input: { effects: Effects; id: number; amount: CareAmountRequest | null } }) => input.effects.update(input.id, input.amount)),
    remove: fromPromise(({ input }: { input: { effects: Effects; id: number } }) => input.effects.remove(input.id)),
  },
  actions: {
    clearError: assign({ error: null }),
    edit: assign({ entry: ({ event }) => event.type === "EDIT" ? event.entry : null, error: null }),
    amount: assign({ amount: ({ event }) => event.type === "SAVE" ? event.amount : null, error: null }),
  },
}).createMachine({
  id: "care",
  context: ({ input }) => ({ effects: input, entry: null, amount: null, error: null, revision: 0 }),
  initial: "idle",
  states: {
    idle: { on: { LOG: { target: "logging", actions: "clearError" }, EDIT: { target: "editing", actions: "edit" } } },
    logging: {
      invoke: { src: "log", input: ({ context }) => context.effects,
        onDone: { target: "confirmed", actions: assign({ entry: ({ event }) => event.output, revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "idle", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
    confirmed: {
      after: { 6000: "idle" },
      on: { UNDO: { target: "deleting", actions: "clearError" }, EDIT: { target: "editing", actions: "edit" }, LOG: { target: "logging", actions: "clearError" } },
    },
    editing: {
      on: { CANCEL: "idle", SAVE: { target: "saving", actions: "amount" }, DELETE: { target: "deleting", actions: "clearError" } },
    },
    saving: {
      invoke: { src: "update", input: ({ context }) => ({ effects: context.effects, id: Number(context.entry!.id), amount: context.amount }),
        onDone: { target: "idle", actions: assign({ revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "editing", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
    deleting: {
      invoke: { src: "remove", input: ({ context }) => ({ effects: context.effects, id: Number(context.entry!.id) }),
        onDone: { target: "idle", actions: assign({ entry: null, revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "editing", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
  },
});
