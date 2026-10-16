import { assign, fromPromise, setup } from "xstate";
import type { CareAmountRequest, CareEntry } from "./client.js";

type Effects = {
  log: (occurredAt?: string) => Promise<CareEntry>;
  update: (id: number, amount: CareAmountRequest | null, occurredAt?: string) => Promise<CareEntry>;
  remove: (id: number) => Promise<CareEntry>;
};
export const careMachine = setup({
  types: {
    input: {} as Effects,
    context: {} as { effects: Effects; entry: CareEntry | null; amount: CareAmountRequest | null; occurredAt: string | undefined; error: unknown; revision: number },
    events: {} as { type: "UNDO" | "CANCEL" | "DELETE" } | { type: "LOG"; occurredAt?: string }
      | { type: "EDIT"; entry: CareEntry } | { type: "SAVE"; amount: CareAmountRequest | null; occurredAt?: string } | { type: "SAVE_TIME"; occurredAt: string },
  },
  actors: {
    log: fromPromise(({ input }: { input: { effects: Effects; occurredAt: string | undefined } }) => input.effects.log(input.occurredAt)),
    update: fromPromise(({ input }: { input: { effects: Effects; id: number; amount: CareAmountRequest | null; occurredAt: string | undefined } }) =>
      input.occurredAt ? input.effects.update(input.id, input.amount, input.occurredAt) : input.effects.update(input.id, input.amount)),
    remove: fromPromise(({ input }: { input: { effects: Effects; id: number } }) => input.effects.remove(input.id)),
  },
  actions: {
    clearError: assign({ error: null }),
    logAt: assign({ error: null, occurredAt: ({ event }) => event.type === "LOG" ? event.occurredAt : undefined }),
    edit: assign({ entry: ({ event }) => event.type === "EDIT" ? event.entry : null, error: null }),
    amount: assign({
      amount: ({ event }) => event.type === "SAVE" ? event.amount : null,
      occurredAt: ({ event }) => event.type === "SAVE" || event.type === "SAVE_TIME" ? event.occurredAt : undefined, error: null,
    }),
  },
}).createMachine({
  id: "care",
  context: ({ input }) => ({ effects: input, entry: null, amount: null, occurredAt: undefined, error: null, revision: 0 }),
  initial: "idle",
  states: {
    idle: { on: { LOG: { target: "logging", actions: "logAt" }, EDIT: { target: "editing", actions: "edit" } } },
    logging: {
      invoke: { src: "log", input: ({ context }) => ({ effects: context.effects, occurredAt: context.occurredAt }),
        onDone: { target: "confirmed", actions: assign({ entry: ({ event }) => event.output, revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "idle", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
    confirmed: {
      after: { 6000: "idle" },
      on: { UNDO: { target: "undoing", actions: "clearError" }, EDIT: { target: "editing", actions: "edit" }, LOG: { target: "logging", actions: "logAt" } },
    },
    editing: {
      on: { CANCEL: "idle", SAVE: { target: "saving", actions: "amount" }, SAVE_TIME: { target: "saving", actions: "amount" }, DELETE: { target: "deleting", actions: "clearError" } },
    },
    saving: {
      invoke: { src: "update", input: ({ context }) => ({ effects: context.effects, id: Number(context.entry!.id), amount: context.amount, occurredAt: context.occurredAt }),
        onDone: { target: "idle", actions: assign({ revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "editing", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
    undoing: {
      invoke: { src: "remove", input: ({ context }) => ({ effects: context.effects, id: Number(context.entry!.id) }),
        onDone: { target: "idle", actions: assign({ entry: null, revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "confirmed", actions: assign({ error: ({ event }) => event.error }) },
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
