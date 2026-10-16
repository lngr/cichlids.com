import { describe, expect, it, vi } from "vitest";
import { existingSubscriptionAction } from "./ownership";
import { switchOffWith } from "./switchOffWith";

describe("existingSubscriptionAction", () => {
  const base = { hasSubscription: true, owner: "account-a", accountId: "account-a", permission: "granted" as const, serverAvailable: true };

  it("keeps the server in step for the member who subscribed in this browser", () => {
    expect(existingSubscriptionAction(base)).toBe("resync");
  });

  it("discards a subscription another account left in this browser", () => {
    expect(existingSubscriptionAction({ ...base, accountId: "account-b" })).toBe("discard");
  });

  it("discards a subscription without a known owner", () => {
    expect(existingSubscriptionAction({ ...base, owner: null })).toBe("discard");
  });

  it("does nothing without a subscription", () => {
    expect(existingSubscriptionAction({ ...base, hasSubscription: false })).toBe("none");
  });

  it("does nothing while the server offers no Web Push or the permission is missing", () => {
    expect(existingSubscriptionAction({ ...base, serverAvailable: false })).toBe("none");
    expect(existingSubscriptionAction({ ...base, permission: "denied" })).toBe("none");
  });
});

describe("switchOffWith", () => {
  it("removes the subscription from the browser even when the server cannot be reached", async () => {
    const unsubscribe = vi.fn(async () => true);
    const clearSettings = vi.fn(async () => {});
    const forgetOwner = vi.fn();
    const deleteOnServer = vi.fn(async () => { throw new Error("offline"); });
    await switchOffWith({
      current: async () => ({ endpoint: "https://push.example/1", unsubscribe }),
      deleteOnServer, clearSettings, forgetOwner,
    });
    expect(deleteOnServer).toHaveBeenCalledWith("https://push.example/1");
    expect(unsubscribe).toHaveBeenCalled();
    expect(clearSettings).toHaveBeenCalled();
    expect(forgetOwner).toHaveBeenCalled();
  });

  it("forgets the owner when the browser has no subscription", async () => {
    const forgetOwner = vi.fn();
    const deleteOnServer = vi.fn(async () => {});
    await switchOffWith({ current: async () => null, deleteOnServer, clearSettings: async () => {}, forgetOwner });
    expect(deleteOnServer).not.toHaveBeenCalled();
    expect(forgetOwner).toHaveBeenCalled();
  });
});
