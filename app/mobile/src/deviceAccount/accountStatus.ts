import type { AuthStatus } from "../auth/authContext";
import type { DeviceAccountState } from "./deviceAccount";

export interface AccountStatus {
  status: AuthStatus;
  /** True when the app acts for the device account of this installation. */
  provisional: boolean;
  /** True while a session of an account waits for the device account to hand over its content. */
  registrationPending: boolean;
  accountId: string | null;
}

/**
 * The status the app shows from the session of the account and the device account. The device
 * account stands in while no session exists. A session that arrives while the device account holds
 * a credential waits (status loading) until the registration has handed the content over, so no
 * request creates a profile for the account before that.
 */
export function combineAccountStatus(session: AuthStatus, device: DeviceAccountState | null, sessionAccountId: string | null): AccountStatus {
  const sessionOnly: AccountStatus = {
    status: session,
    provisional: false,
    registrationPending: false,
    accountId: session === "authenticated" ? sessionAccountId : null,
  };
  if (!device || session === "loading") return sessionOnly;
  if (session === "authenticated") {
    if (device.hasCredential === null) return { ...sessionOnly, status: "loading", accountId: null };
    const pending = device.hasCredential && !device.leftBehind;
    return pending ? { status: "loading", provisional: false, registrationPending: true, accountId: null } : sessionOnly;
  }
  switch (device.phase) {
    case "ready":
      return { status: "authenticated", provisional: true, registrationPending: false, accountId: `device:${device.profileId}` };
    case "offline":
      return sessionOnly;
    default:
      return { ...sessionOnly, status: "loading" };
  }
}
