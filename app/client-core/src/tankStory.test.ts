import { describe, expect, it } from "vitest";
import { createActor } from "xstate";
import { tankStoryMachine } from "./tankStory.js";

describe("tank story and time-machine", () => {
  it("opens, plays, pauses on scrub, clamps keyboard steps and closes paused", () => {
    const actor = createActor(tankStoryMachine, { input: { count: 3 } }).start();
    actor.send({ type: "OPEN" });
    actor.send({ type: "PLAY" });
    expect(actor.getSnapshot().matches({ story: { player: "playing" } })).toBe(true);
    actor.send({ type: "TICK" });
    expect(actor.getSnapshot().context.index).toBe(1);
    actor.send({ type: "SEEK", index: 99 });
    expect(actor.getSnapshot().context.index).toBe(2);
    expect(actor.getSnapshot().matches({ story: { player: "paused" } })).toBe(true);
    actor.send({ type: "SEEK", index: -1 });
    expect(actor.getSnapshot().context.index).toBe(0);
    actor.send({ type: "PLAY" });
    actor.send({ type: "TICK" });
    actor.send({ type: "CLOSE" });
    expect(actor.getSnapshot().matches({ story: "preview" })).toBe(true);
    actor.send({ type: "OPEN" });
    expect(actor.getSnapshot().context.index).toBe(0);
    actor.send({ type: "CLOSE" });
    actor.send({ type: "CARE" });
    expect(actor.getSnapshot().matches("care")).toBe(true);
    actor.stop();
  });
  it("stops at the last frame and prevents playback with fewer than two photos", () => {
    for (const count of [0, 1, 2]) {
      const actor = createActor(tankStoryMachine, { input: { count } }).start();
      actor.send({ type: "OPEN" });
      actor.send({ type: "PLAY" });
      actor.send({ type: "TICK" });
      actor.send({ type: "TICK" });
      expect(actor.getSnapshot().context.index).toBe(Math.max(0, count - 1));
      expect(actor.getSnapshot().matches({ story: { player: "playing" } })).toBe(false);
      actor.stop();
    }
  });
});
