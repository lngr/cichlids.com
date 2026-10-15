import { describe, expect, it, vi } from "vitest";
import { createActor } from "xstate";
import { navigationMachine } from "@cichlids/client-core";
import { bindNavigation, pathFromUrl } from "./adapter";
function fixture(path = "/today") {
  const actor = createActor(navigationMachine).start();
  actor.send({ type: "EXTERNAL", path });
  const port = { currentPath: () => path, push: vi.fn(), replace: vi.fn(), dismissTo: vi.fn(), back: vi.fn(), canGoBack: () => true };
  const adapter = bindNavigation(actor, port);
  return { actor, adapter, port };
}
describe("navigation platform adapter", () => {
  it("projects machine transitions into router commands and ignores sheet-only changes", () => {
    const { actor, port, adapter } = fixture();
    actor.send({ type: "CREATE" });
    expect(port.push).not.toHaveBeenCalled();
    actor.send({ type: "PHOTO", authenticated: true });
    expect(port.push).toHaveBeenCalledExactlyOnceWith("/upload");
    adapter.dispose();
  });
  it("sends a native swipe/pop to the machine before projecting its back target", async () => {
    const { actor, port, adapter } = fixture("/gallery/photo");
    await Promise.resolve();
    const event = { data: { action: { type: "POP" } }, preventDefault: vi.fn() };
    adapter.beforeRemove(event);
    expect(event.preventDefault).toHaveBeenCalledOnce();
    expect(actor.getSnapshot().context.path).toBe("/gallery");
    expect(port.dismissTo).toHaveBeenLastCalledWith("/gallery");
    adapter.dispose();
  });
  it("handles browser history, initial auth callbacks and incoming links", () => {
    const { actor, port, adapter } = fixture("/auth");
    expect(port.replace).toHaveBeenCalledWith("/me");
    adapter.external("/species/frontosa");
    expect(actor.getSnapshot().matches({ viewing: "species" })).toBe(true);
    expect(port.replace).toHaveBeenLastCalledWith("/species/frontosa");
    adapter.dispose();
  });
  it("normalizes universal and custom-scheme links including auth callbacks", () => {
    expect(pathFromUrl("cichlids://gallery/frontosa?species=frontosa")).toBe("/gallery/frontosa?species=frontosa");
    expect(pathFromUrl("cichlids://auth?code=abc")).toBe("/auth?code=abc");
    expect(pathFromUrl("https://cichlids.com/tanks/12")).toBe("/tanks/12");
  });
  it("dismisses create before browser back leaves its view", () => {
    const { actor, adapter, port } = fixture("/fish");
    actor.send({ type: "CREATE" });
    adapter.browserPop("/today");
    expect(actor.getSnapshot().matches("viewing")).toBe(true);
    expect(actor.getSnapshot().context.path).toBe("/fish");
    expect(port.replace).toHaveBeenLastCalledWith("/fish");
    adapter.dispose();
  });
  it("owns Android back and dismisses the sheet before leaving its view", () => {
    const { actor, adapter } = fixture();
    expect(adapter.hardwareBack()).toBe(false);
    actor.send({ type: "CREATE" });
    expect(adapter.hardwareBack()).toBe(true);
    expect(actor.getSnapshot().matches("viewing")).toBe(true);
    expect(actor.getSnapshot().context.path).toBe("/today");
    adapter.dispose();
  });
});
