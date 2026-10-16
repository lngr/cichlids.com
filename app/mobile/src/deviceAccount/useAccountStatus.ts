import { useEffect, useSyncExternalStore } from "react";
import { AppState } from "react-native";
import { setDeviceAuth, setDeviceRefusedListener } from "../api/client";
import type { AuthStatus } from "../auth/authContext";
import { combineAccountStatus, type AccountStatus } from "./accountStatus";
import type { DeviceAccountState } from "./deviceAccount";
import { deviceAccount } from "./instance";

const NO_ACCOUNT: DeviceAccountState | null = null;
const noSubscription = () => () => {};

/** The device account of this installation; null where the app keeps none (the web app outside the test build). */
export { deviceAccount };

/** The state of the device account; null where the app keeps none. */
export function useDeviceAccountState(): DeviceAccountState | null {
  return useSyncExternalStore(
    deviceAccount ? deviceAccount.subscribe : noSubscription,
    () => (deviceAccount ? deviceAccount.getState() : NO_ACCOUNT),
    () => NO_ACCOUNT,
  );
}

/**
 * Combines the session of the account (Keycloak) with the device account. Without a session the
 * device account registers the installation and the API client uses its token; with a session the
 * device account rests and only signs the proof of a registration. The returned status is what
 * the rest of the app sees as the status of the member.
 */
export function useAccountStatus(sessionStatus: AuthStatus, sessionAccountId: string | null): AccountStatus & { leftBehind: boolean } {
  const device = useDeviceAccountState();

  useEffect(() => {
    if (!deviceAccount) return;
    const account = deviceAccount;
    setDeviceAuth(() => account.auth(), sessionStatus === "anonymous");
    if (sessionStatus === "anonymous") account.start();
    else {
      account.stop();
      if (sessionStatus === "authenticated") void account.inspect();
    }
    return () => setDeviceAuth(null, false);
  }, [sessionStatus]);

  useEffect(() => {
    if (!deviceAccount) return;
    const account = deviceAccount;
    // A credential unknown to the API (removed by the cleanup of unused accounts) starts a new device account.
    setDeviceRefusedListener(() => {
      if (sessionStatus !== "anonymous") return;
      void account.discard().then(() => account.start());
    });
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") account.retryNow();
    });
    return () => {
      subscription.remove();
      setDeviceRefusedListener(null);
    };
  }, [sessionStatus]);

  return { ...combineAccountStatus(sessionStatus, device, sessionAccountId), leftBehind: device?.leftBehind ?? false };
}
