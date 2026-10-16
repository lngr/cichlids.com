import type { NotificationPermissionState } from "./support";

const OWNER_KEY = "cichlids.webPush.owner";

export type ExistingSubscriptionAction = "resync" | "discard" | "none";

/**
 * What to do with a push subscription that already exists in this browser when the reminder
 * setting opens. Only the account that agreed to the explanation and subscribed in this browser
 * keeps it; a subscription another account left behind, or one without a known owner, is
 * discarded, so a member never receives reminders without their own consent.
 */
export function existingSubscriptionAction(input: {
  hasSubscription: boolean;
  owner: string | null;
  accountId: string | null;
  permission: NotificationPermissionState;
  serverAvailable: boolean;
}): ExistingSubscriptionAction {
  if (!input.hasSubscription || !input.serverAvailable || input.permission !== "granted") return "none";
  return input.owner !== null && input.owner === input.accountId ? "resync" : "discard";
}

/** The account that subscribed in this browser, or null when none is known. */
export function readOwner(): string | null {
  try {
    return globalThis.localStorage?.getItem(OWNER_KEY) ?? null;
  } catch {
    return null;
  }
}

/** Remembers the account that subscribed in this browser. */
export function rememberOwner(accountId: string): void {
  try {
    globalThis.localStorage?.setItem(OWNER_KEY, accountId);
  } catch {
    // Without storage the subscription has no known owner and is discarded on the next visit.
  }
}

/** Forgets the account that subscribed in this browser. */
export function forgetOwner(): void {
  try {
    globalThis.localStorage?.removeItem(OWNER_KEY);
  } catch {
    // Nothing to forget without storage.
  }
}
