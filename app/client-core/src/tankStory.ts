import { assign, setup } from "xstate";

export const tankStoryMachine = setup({
  types: {
    context: {} as { count: number; index: number },
    input: {} as { count: number },
    events: {} as { type: "STORY" | "CARE" | "OPEN" | "CLOSE" | "PLAY" | "PAUSE" | "TICK" } | { type: "SEEK"; index: number },
  },
  guards: {
    playable: ({ context }) => context.count >= 2,
    last: ({ context }) => context.index >= context.count - 1,
  },
  actions: {
    rewind: assign({ index: 0 }),
    seek: assign({ index: ({ context, event }) => event.type === "SEEK" ? Math.max(0, Math.min(context.count - 1, Math.round(event.index))) : context.index }),
    advance: assign({ index: ({ context }) => Math.min(context.count - 1, context.index + 1) }),
    replay: assign({ index: ({ context }) => context.index >= context.count - 1 ? 0 : context.index }),
  },
}).createMachine({
  id: "tankStory",
  context: ({ input }) => ({ count: input.count, index: 0 }),
  initial: "story",
  states: {
    story: {
      initial: "preview",
      on: { CARE: "care" },
      states: {
        preview: { on: { OPEN: { target: "player", guard: "playable", actions: "rewind" } } },
        player: {
          initial: "paused",
          on: { CLOSE: "preview", SEEK: { target: ".paused", actions: "seek" } },
          states: {
            paused: { on: { PLAY: { target: "playing", guard: "playable", actions: "replay" } } },
            playing: {
              on: { PAUSE: "paused", TICK: [{ guard: "last", target: "paused" }, { actions: "advance" }] },
            },
          },
        },
      },
    },
    care: { on: { STORY: "story" } },
  },
});
