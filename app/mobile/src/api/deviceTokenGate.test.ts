import { describe, expect, it, vi } from "vitest";
import { DeviceAuthError, type DeviceAuth } from "@cichlids/client-core";
import { createDeviceTokenGate } from "./deviceTokenGate";

function auth(overrides: Partial<DeviceAuth> = {}): DeviceAuth {
  return {
    getAccessToken: async () => "device-token",
    signProof: async () => ({ challenge: "c", credentialId: "id", timestamp: 1, signature: "s" }),
    ensureRegistered: async () => ({ credentialId: "id", profileId: 1 }),
    invalidate: vi.fn(),
    ...overrides,
  };
}

function setup(current: DeviceAuth | null, active: boolean) {
  const onRefused = vi.fn();
  const gate = createDeviceTokenGate({ getAuth: () => current, isActive: () => active, onRefused });
  return { gate, onRefused };
}

describe("the device token gate", () => {
  it("hands out the device token while the device account acts", async () => {
    expect(await setup(auth(), true).gate.getAccessToken()).toBe("device-token");
  });

  it("hands out no token while a session of the account acts", async () => {
    const getAccessToken = vi.fn(async () => "device-token");
    expect(await setup(auth({ getAccessToken }), false).gate.getAccessToken()).toBeUndefined();
    expect(getAccessToken).not.toHaveBeenCalled();
  });

  it("hands out no token without a device authentication", async () => {
    expect(await setup(null, true).gate.getAccessToken()).toBeUndefined();
  });

  it("lets the request go on without a token when the exchange fails for lack of a network", async () => {
    const { gate, onRefused } = setup(auth({ getAccessToken: async () => { throw new TypeError("Network request failed"); } }), true);
    expect(await gate.getAccessToken()).toBeUndefined();
    expect(onRefused).not.toHaveBeenCalled();
  });

  it("reports a refusal of the credential, which no retry heals", async () => {
    const refused = new DeviceAuthError(401, "device_auth_refused", "Refused.");
    const { gate, onRefused } = setup(auth({ getAccessToken: async () => { throw refused; } }), true);
    expect(await gate.getAccessToken()).toBeUndefined();
    expect(onRefused).toHaveBeenCalledTimes(1);
  });

  it("signs a proof with the current device authentication even while a session acts", async () => {
    const signProof = vi.fn(async () => ({ challenge: "c", credentialId: "id", timestamp: 1, signature: "s" }));
    const { gate } = setup(auth({ signProof }), false);
    await gate.signProof();
    expect(signProof).toHaveBeenCalledTimes(1);
  });

  it("refuses a proof without a device authentication", async () => {
    await expect(setup(null, false).gate.signProof()).rejects.toThrow(/device/i);
  });

  it("drops the cached token of the current device authentication", () => {
    const current = auth();
    setup(current, true).gate.invalidate();
    expect(current.invalidate).toHaveBeenCalledTimes(1);
  });
});
