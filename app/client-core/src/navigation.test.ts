import { describe, expect, it } from "vitest";
import { createActor } from "xstate";
import { navigationMachine, selectedTab } from "./navigation.js";

const start = () => createActor(navigationMachine).start();
describe("navigation", () => {
  it("starts on Today and selects each view through tab events", () => {
    const actor = start();
    expect(actor.getSnapshot().context.path).toBe("/today");
    for (const tab of ["discover", "fish", "me", "today"] as const) {
      actor.send({ type: "TAB", tab });
      expect(actor.getSnapshot().context.path).toBe(`/${tab}`);
      expect(selectedTab(actor.getSnapshot().context.path)).toBe(tab);
    }
  });
  it("opens and dismisses create without losing the current view", () => {
    const actor = start();
    actor.send({ type: "OPEN", path: "/gallery?species=frontosa" });
    actor.send({ type: "CREATE" });
    expect(actor.getSnapshot().matches("creating")).toBe(true);
    expect(actor.getSnapshot().context.path).toBe("/gallery?species=frontosa");
    actor.send({ type: "BACK" });
    expect(actor.getSnapshot().matches("viewing")).toBe(true);
    expect(actor.getSnapshot().context.path).toBe("/gallery?species=frontosa");
  });
  it("routes photo upload through the login view for guests", () => {
    for (const authenticated of [false, true]) {
      const actor = start();
      actor.send({ type: "CREATE" });
      actor.send({ type: "PHOTO", authenticated });
      expect(actor.getSnapshot().context.path).toBe(authenticated ? "/upload" : "/me");
      expect(actor.getSnapshot().matches("viewing")).toBe(true);
    }
  });
  it("owns detail history, back, home and replacement after publishing", () => {
    const actor = start();
    actor.send({ type: "TAB", tab: "discover" });
    for (const path of ["/tanks", "/tanks/12", "/profile/7"]) actor.send({ type: "OPEN", path });
    actor.send({ type: "BACK" });
    expect(actor.getSnapshot().context.path).toBe("/tanks/12");
    actor.send({ type: "DISMISS", path: "/gallery/published" });
    expect(actor.getSnapshot().context.path).toBe("/gallery/published");
    expect(actor.getSnapshot().context.mode).toBe("dismiss");
    actor.send({ type: "BACK" });
    expect(actor.getSnapshot().context.path).toBe("/tanks");
    actor.send({ type: "HOME" });
    expect(actor.getSnapshot().context.path).toBe("/today");
  });
  it("returns a directly opened tab to Today and ignores unavailable create actions", () => {
    const actor = start();
    actor.send({ type: "EXTERNAL", path: "/fish" });
    actor.send({ type: "PHOTO", authenticated: true });
    expect(actor.getSnapshot().context.path).toBe("/fish");
    actor.send({ type: "BACK" });
    expect(actor.getSnapshot().context.path).toBe("/today");
    actor.send({ type: "BACK" });
    expect(actor.getSnapshot().context.path).toBe("/today");
  });
  it("accepts browser history and deep links centrally and resolves legacy tab ownership", () => {
    const actor = start();
    for (const [path, tab] of [["/onboarding", "me"], ["/gallery/a", "discover"], ["/tanks/1", "discover"], ["/community/thread/1", "discover"], ["/species/frontosa", "fish"], ["/profile/1", "me"]]) {
      actor.send({ type: "EXTERNAL", path });
      expect(actor.getSnapshot().context.path).toBe(path);
      expect(selectedTab(path)).toBe(tab);
    }
    actor.send({ type: "EXTERNAL", path: "/auth" });
    expect(actor.getSnapshot().context.path).toBe("/me");
    actor.send({ type: "EXTERNAL", path: "/" });
    expect(actor.getSnapshot().context.path).toBe("/today");
  });
});


it("routes Water change from the create sheet through tank selection", () => {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "CREATE" });
  actor.send({ type: "WATER_CHANGE", authenticated: true });
  expect(actor.getSnapshot().context.path).toBe("/care");
  actor.send({ type: "OPEN", path: "/tanks/7?view=care" });
  expect(actor.getSnapshot().context.path).toBe("/tanks/7?view=care");
  actor.stop();
});

it("opens the to-do list as its own view of the Me tab and returns to where it was opened", () => {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "OPEN", path: "/todos" });
  expect(actor.getSnapshot().context.path).toBe("/todos");
  expect(actor.getSnapshot().matches({ viewing: "todos" })).toBe(true);
  expect(selectedTab("/todos")).toBe("me");
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/today");
  actor.send({ type: "EXTERNAL", path: "/todos" });
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/me");
  actor.stop();
});

it("opens the media library as its own view of the Me tab and returns to where it was opened", () => {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "OPEN", path: "/media" });
  expect(actor.getSnapshot().context.path).toBe("/media");
  expect(actor.getSnapshot().matches({ viewing: "media" })).toBe(true);
  expect(selectedTab("/media")).toBe("me");
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/today");
  actor.send({ type: "EXTERNAL", path: "/media" });
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/me");
  actor.stop();
});

it("opens the story list and the editor as views of the Me tab and returns to the list", () => {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "OPEN", path: "/stories" });
  actor.send({ type: "OPEN", path: "/stories/12" });
  expect(actor.getSnapshot().matches({ viewing: "stories" })).toBe(true);
  expect(selectedTab("/stories/12")).toBe("me");
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/stories");
  actor.send({ type: "EXTERNAL", path: "/stories/12" });
  actor.send({ type: "BACK" });
  expect(actor.getSnapshot().context.path).toBe("/me");
  actor.stop();
});
