import { assign, fromPromise, setup } from "xstate";
import type { CareEntry, CareTodo, UploadFile } from "./client.js";

/** The longest note of a care entry, in characters. */
export const NOTE_MAX_LENGTH = 2000;
/** The longest text of a to-do, in characters. */
export const TODO_MAX_LENGTH = 200;
/** The most photos one care entry holds. */
export const MAX_PHOTOS = 6;

/** The note as the API stores it: trimmed, or null when it is blank and therefore removes the note. */
export const normalizeNote = (text: string): string | null => text.trim() || null;
/** Whether a note fits the limit after trimming. */
export const isValidNote = (text: string): boolean => text.trim().length <= NOTE_MAX_LENGTH;
/** The to-do text as the API stores it, or null when it is blank or longer than the limit. */
export function normalizeTodoText(text: string): string | null {
  const trimmed = text.trim();
  return trimmed.length > 0 && trimmed.length <= TODO_MAX_LENGTH ? trimmed : null;
}

type AttachmentEffects = {
  note: (entryId: number, note: string | null) => Promise<CareEntry>;
  addPhoto: (entryId: number, file: UploadFile) => Promise<CareEntry>;
  removePhoto: (entryId: number, photoId: number) => Promise<CareEntry>;
  addTodo: (entryId: number, text: string) => Promise<CareTodo>;
};
type AttachmentCommand =
  | { type: "SAVE_NOTE"; entryId: number; note: string | null }
  | { type: "ADD_PHOTO"; entryId: number; file: UploadFile }
  | { type: "REMOVE_PHOTO"; entryId: number; photoId: number }
  | { type: "ADD_TODO"; entryId: number; text: string };

/**
 * The changes that enrich a care entry: its note, its photos and its to-dos. One change runs at a
 * time and commands that arrive meanwhile are ignored. A finished change raises the revision, a
 * failed one keeps its error until the next change starts.
 */
export const careAttachmentsMachine = setup({
  types: {
    input: {} as AttachmentEffects,
    context: {} as { effects: AttachmentEffects; command: AttachmentCommand | null; error: unknown; revision: number },
    events: {} as AttachmentCommand,
  },
  actors: {
    run: fromPromise(async ({ input }: { input: { effects: AttachmentEffects; command: AttachmentCommand } }) => {
      const { effects, command } = input;
      switch (command.type) {
        case "SAVE_NOTE": return effects.note(command.entryId, command.note);
        case "ADD_PHOTO": return effects.addPhoto(command.entryId, command.file);
        case "REMOVE_PHOTO": return effects.removePhoto(command.entryId, command.photoId);
        case "ADD_TODO": return effects.addTodo(command.entryId, command.text);
      }
    }),
  },
  actions: {
    start: assign({ command: ({ event }) => event, error: null }),
  },
}).createMachine({
  id: "careAttachments",
  context: ({ input }) => ({ effects: input, command: null, error: null, revision: 0 }),
  initial: "idle",
  states: {
    idle: { on: { SAVE_NOTE: { target: "working", actions: "start" }, ADD_PHOTO: { target: "working", actions: "start" },
      REMOVE_PHOTO: { target: "working", actions: "start" }, ADD_TODO: { target: "working", actions: "start" } } },
    working: {
      invoke: {
        src: "run", input: ({ context }) => ({ effects: context.effects, command: context.command! }),
        onDone: { target: "idle", actions: assign({ revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "idle", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
  },
});

type TodoEffects = {
  add: (tankId: number, text: string) => Promise<CareTodo>;
  setDone: (id: number, done: boolean) => Promise<CareTodo>;
  remove: (id: number) => Promise<CareTodo>;
};
type TodoCommand =
  | { type: "ADD"; tankId: number; text: string }
  | { type: "SET_DONE"; id: number; done: boolean }
  | { type: "REMOVE"; id: number };

/**
 * The changes of the member's to-do list: adding a to-do to a tank, checking one off, reopening
 * it and deleting it. One change runs at a time; a finished change raises the revision, a failed
 * one keeps its error until the next change starts.
 */
export const todoListMachine = setup({
  types: {
    input: {} as TodoEffects,
    context: {} as { effects: TodoEffects; command: TodoCommand | null; error: unknown; revision: number },
    events: {} as TodoCommand,
  },
  actors: {
    run: fromPromise(async ({ input }: { input: { effects: TodoEffects; command: TodoCommand } }) => {
      const { effects, command } = input;
      switch (command.type) {
        case "ADD": return effects.add(command.tankId, command.text);
        case "SET_DONE": return effects.setDone(command.id, command.done);
        case "REMOVE": return effects.remove(command.id);
      }
    }),
  },
  actions: {
    start: assign({ command: ({ event }) => event, error: null }),
  },
}).createMachine({
  id: "todoList",
  context: ({ input }) => ({ effects: input, command: null, error: null, revision: 0 }),
  initial: "idle",
  states: {
    idle: { on: { ADD: { target: "working", actions: "start" }, SET_DONE: { target: "working", actions: "start" }, REMOVE: { target: "working", actions: "start" } } },
    working: {
      invoke: {
        src: "run", input: ({ context }) => ({ effects: context.effects, command: context.command! }),
        onDone: { target: "idle", actions: assign({ revision: ({ context }) => context.revision + 1 }) },
        onError: { target: "idle", actions: assign({ error: ({ event }) => event.error }) },
      },
    },
  },
});
