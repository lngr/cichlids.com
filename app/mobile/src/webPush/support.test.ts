import { describe, expect, it } from "vitest";
import { panelState, webPushSupport } from "./support";

const facts = { hasServiceWorker: true, hasPushManager: true, hasNotification: true, isIos: false, isStandalone: false };

describe("webPushSupport", () => {
  it("supports a browser with service workers, push and notifications", () => {
    expect(webPushSupport(facts)).toBe("supported");
  });
  it("reports a browser without one of the three as unsupported", () => {
    expect(webPushSupport({ ...facts, hasServiceWorker: false })).toBe("unsupported");
    expect(webPushSupport({ ...facts, hasPushManager: false })).toBe("unsupported");
    expect(webPushSupport({ ...facts, hasNotification: false })).toBe("unsupported");
  });
  it("asks iOS users outside a home-screen web app to add the app to the home screen", () => {
    expect(webPushSupport({ ...facts, isIos: true })).toBe("needs_home_screen");
    expect(webPushSupport({ ...facts, isIos: true, hasPushManager: false })).toBe("needs_home_screen");
  });
  it("supports a home-screen web app on iOS", () => {
    expect(webPushSupport({ ...facts, isIos: true, isStandalone: true })).toBe("supported");
  });
});

describe("panelState", () => {
  const base = { support: "supported" as const, serverAvailable: true as boolean | null, permission: "default" as const, subscribed: false, explaining: false };
  it("waits for the server answer", () => {
    expect(panelState({ ...base, serverAvailable: null })).toBe("loading");
  });
  it("shows the unsupported and home-screen hints before anything else", () => {
    expect(panelState({ ...base, support: "unsupported" })).toBe("unsupported");
    expect(panelState({ ...base, support: "needs_home_screen", serverAvailable: null })).toBe("needs_home_screen");
  });
  it("tells that the server does not send browser notifications", () => {
    expect(panelState({ ...base, serverAvailable: false })).toBe("unavailable");
  });
  it("is off by default and shows the explanation only after the member asked to switch on", () => {
    expect(panelState(base)).toBe("off");
    expect(panelState({ ...base, explaining: true })).toBe("explain");
  });
  it("is on when this browser holds a subscription and the permission is granted", () => {
    expect(panelState({ ...base, permission: "granted", subscribed: true })).toBe("on");
    expect(panelState({ ...base, permission: "granted", subscribed: false })).toBe("off");
  });
  it("shows a calm hint when the browser blocks notifications", () => {
    expect(panelState({ ...base, permission: "denied" })).toBe("denied");
    expect(panelState({ ...base, permission: "denied", subscribed: true })).toBe("denied");
  });
});
