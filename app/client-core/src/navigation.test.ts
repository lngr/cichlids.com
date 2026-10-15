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
    actor.send({ type: "REPLACE", path: "/gallery/published" });
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
    for (const [path, tab] of [["/gallery/a", "discover"], ["/tanks/1", "discover"], ["/community/thread/1", "discover"], ["/species/frontosa", "fish"], ["/profile/1", "me"]]) {
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
