import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DeviceAuth, DeviceCredential } from "@cichlids/client-core";
import { createDeviceAccount, type DeviceAccountDependencies } from "./deviceAccount";

const credential: DeviceCredential = { credentialId: "c-1", profileId: 42 };

function setup(overrides: Partial<DeviceAccountDependencies> = {}) {
  const flags = new Map<string, string>();
  const stored: { credential: DeviceCredential | undefined } = { credential: undefined };
  const ensureRegistered = vi.fn(async () => {
    stored.credential = credential;
    return credential;
  });
  const auths: DeviceAuth[] = [];
  const dependencies: DeviceAccountDependencies = {
    createAuth: () => {
      const auth: DeviceAuth = {
        ensureRegistered,
        getAccessToken: async () => "token",
        signProof: async () => ({ challenge: "c", credentialId: "c-1", timestamp: 1, signature: "s" }),
        invalidate: () => {},
      };
      auths.push(auth);
      return auth;
    },
    credentials: {
      load: async () => stored.credential,
      save: async (value) => { stored.credential = value; },
      clear: async () => { stored.credential = undefined; },
    },
    flags: {
      get: async (key) => flags.get(key) ?? null,
      set: async (key, value) => { flags.set(key, value); },
      remove: async (key) => { flags.delete(key); },
    },
    destroyKey: vi.fn(async () => {}),
    firstAttemptPatienceMs: 6000,
    retryDelaysMs: [1000, 5000],
    ...overrides,
  };
  return { account: createDeviceAccount(dependencies), ensureRegistered, stored, flags, dependencies, auths };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("the device account", () => {
  it("starts idle", () => {
    const { account } = setup();
    expect(account.getState()).toMatchObject({ phase: "idle", profileId: null, hasCredential: null, leftBehind: false });
  });

  it("registers on start and becomes ready with the profile of the installation", async () => {
    const { account, ensureRegistered } = setup();
    const seen: string[] = [];
    account.subscribe(() => seen.push(account.getState().phase));
    account.start();
    expect(account.getState().phase).toBe("starting");
    await vi.advanceTimersByTimeAsync(0);
    expect(account.getState()).toMatchObject({ phase: "ready", profileId: 42, hasCredential: true });
    expect(ensureRegistered).toHaveBeenCalledTimes(1);
    expect(seen).toEqual(["starting", "ready"]);
  });

  it("does not block when the first attempt takes long, and becomes ready when it succeeds", async () => {
    let finish!: (value: DeviceCredential) => void;
    const { account, ensureRegistered } = setup();
    ensureRegistered.mockImplementationOnce(() => new Promise<DeviceCredential>((resolve) => { finish = resolve; }));
    account.start();
    await vi.advanceTimersByTimeAsync(6000);
    expect(account.getState().phase).toBe("offline");
    finish(credential);
    await vi.advanceTimersByTimeAsync(0);
    expect(account.getState().phase).toBe("ready");
  });

  it("goes offline when the registration fails and retries later with growing delays", async () => {
    const { account, ensureRegistered } = setup();
    ensureRegistered.mockRejectedValueOnce(new Error("offline")).mockRejectedValueOnce(new Error("offline"));
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(account.getState().phase).toBe("offline");
    await vi.advanceTimersByTimeAsync(999);
    expect(ensureRegistered).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(ensureRegistered).toHaveBeenCalledTimes(2);
    expect(account.getState().phase).toBe("offline");
    await vi.advanceTimersByTimeAsync(4999);
    expect(ensureRegistered).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(ensureRegistered).toHaveBeenCalledTimes(3);
    expect(account.getState().phase).toBe("ready");
  });

  it("keeps the longest delay for further retries", async () => {
    const { account, ensureRegistered } = setup();
    ensureRegistered.mockRejectedValue(new Error("offline"));
    account.start();
    await vi.advanceTimersByTimeAsync(1000 + 5000 + 5000 + 5000);
    expect(ensureRegistered).toHaveBeenCalledTimes(5);
  });

  it("retries at once on request", async () => {
    const { account, ensureRegistered } = setup();
    ensureRegistered.mockRejectedValueOnce(new Error("offline"));
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    account.retryNow();
    await vi.advanceTimersByTimeAsync(0);
    expect(ensureRegistered).toHaveBeenCalledTimes(2);
    expect(account.getState().phase).toBe("ready");
  });

  it("stops retrying when stopped", async () => {
    const { account, ensureRegistered } = setup();
    ensureRegistered.mockRejectedValue(new Error("offline"));
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    account.stop();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(ensureRegistered).toHaveBeenCalledTimes(1);
    expect(account.getState().phase).toBe("idle");
  });

  it("ignores a second start while running", async () => {
    const { account, ensureRegistered } = setup();
    account.start();
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(ensureRegistered).toHaveBeenCalledTimes(1);
  });

  it("an inspection reports a stored credential and the left behind flag without any request", async () => {
    const { account, stored, flags, ensureRegistered } = setup();
    stored.credential = credential;
    flags.set("cichlids.device.leftBehind", "1");
    await account.inspect();
    expect(account.getState()).toMatchObject({ phase: "idle", hasCredential: true, profileId: 42, leftBehind: true });
    expect(ensureRegistered).not.toHaveBeenCalled();
  });

  it("an inspection without a credential reports none", async () => {
    const { account } = setup();
    await account.inspect();
    expect(account.getState().hasCredential).toBe(false);
  });

  it("remembers that the content was left behind", async () => {
    const { account, flags } = setup();
    await account.setLeftBehind(true);
    expect(account.getState().leftBehind).toBe(true);
    expect(flags.get("cichlids.device.leftBehind")).toBe("1");
    await account.setLeftBehind(false);
    expect(account.getState().leftBehind).toBe(false);
    expect(flags.has("cichlids.device.leftBehind")).toBe(false);
  });

  it("discards the credential, the key and the flags after a registration, and registers a new installation on the next start", async () => {
    const { account, stored, flags, dependencies, auths, ensureRegistered } = setup();
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    await account.setLeftBehind(true);
    const before = account.auth();
    await account.discard();
    expect(stored.credential).toBeUndefined();
    expect(flags.size).toBe(0);
    expect(dependencies.destroyKey).toHaveBeenCalledTimes(1);
    expect(account.getState()).toMatchObject({ phase: "idle", hasCredential: false, profileId: null, leftBehind: false });
    expect(account.auth()).not.toBe(before);
    expect(auths).toHaveLength(2);
    account.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(ensureRegistered).toHaveBeenCalledTimes(2);
  });
});
