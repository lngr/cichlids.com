import type { BrowserFacts, NotificationPermissionState } from "./support";
import type { WorkerSettings } from "./workerSettings";

// Web Push exists only in the browser build; the native app schedules local notifications.

export function browserFacts(): BrowserFacts {
  return { hasServiceWorker: false, hasPushManager: false, hasNotification: false, isIos: false, isStandalone: false };
}

export function notificationPermission(): NotificationPermissionState {
  return "default";
}

export async function currentSubscription(): Promise<PushSubscription | null> {
  return null;
}

export async function enableInBrowser(_publicKey: string): Promise<PushSubscription> {
  throw new Error("Web Push is not available on this platform.");
}

export async function storeWorkerSettings(_settings: WorkerSettings): Promise<void> {}

export async function clearWorkerSettings(): Promise<void> {}
