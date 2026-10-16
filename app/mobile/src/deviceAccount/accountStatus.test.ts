import { describe, expect, it } from "vitest";
import { combineAccountStatus } from "./accountStatus";
import type { DeviceAccountState } from "./deviceAccount";

const state = (change: Partial<DeviceAccountState> = {}): DeviceAccountState => ({
  phase: "idle", profileId: null, hasCredential: null, leftBehind: false, ...change,
});

describe("combineAccountStatus", () => {
  it("is the session status when the installation keeps no device account", () => {
    expect(combineAccountStatus("anonymous", null, "kc-1")).toEqual({ status: "anonymous", provisional: false, registrationPending: false, accountId: null });
    expect(combineAccountStatus("authenticated", null, "kc-1")).toEqual({ status: "authenticated", provisional: false, registrationPending: false, accountId: "kc-1" });
    expect(combineAccountStatus("loading", null, null).status).toBe("loading");
  });

  it("waits while the session loads", () => {
    expect(combineAccountStatus("loading", state({ phase: "ready", hasCredential: true, profileId: 3 }), null).status).toBe("loading");
  });

  it("waits for the first registration of an installation without a session", () => {
    expect(combineAccountStatus("anonymous", state(), null).status).toBe("loading");
    expect(combineAccountStatus("anonymous", state({ phase: "starting" }), null).status).toBe("loading");
  });

  it("is an authenticated provisional account once the installation is registered", () => {
    expect(combineAccountStatus("anonymous", state({ phase: "ready", profileId: 42, hasCredential: true }), null)).toEqual({
      status: "authenticated", provisional: true, registrationPending: false, accountId: "device:42",
    });
  });

  it("is anonymous while registering is offline, so the app keeps working as a visitor", () => {
    expect(combineAccountStatus("anonymous", state({ phase: "offline" }), null)).toMatchObject({ status: "anonymous", provisional: false });
  });

  it("holds a session with a device credential back until the registration is done", () => {
    expect(combineAccountStatus("authenticated", state({ hasCredential: true, profileId: 42 }), "kc-1")).toEqual({
      status: "loading", provisional: false, registrationPending: true, accountId: null,
    });
  });

  it("waits for the stored credential to be read", () => {
    expect(combineAccountStatus("authenticated", state(), "kc-1")).toMatchObject({ status: "loading", registrationPending: false });
  });

  it("lets a session through when the content stayed on the device", () => {
    expect(combineAccountStatus("authenticated", state({ hasCredential: true, leftBehind: true }), "kc-1")).toEqual({
      status: "authenticated", provisional: false, registrationPending: false, accountId: "kc-1",
    });
  });

  it("lets a session through when the installation has no credential", () => {
    expect(combineAccountStatus("authenticated", state({ hasCredential: false }), "kc-1")).toMatchObject({ status: "authenticated", registrationPending: false });
  });
});
