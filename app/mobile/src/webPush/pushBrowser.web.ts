import { PermissionDeniedError, type BrowserFacts, type NotificationPermissionState } from "./support";
import { urlBase64ToBytes, WORKER_SETTINGS_CACHE, WORKER_SETTINGS_URL, type WorkerSettings } from "./workerSettings";

const WORKER_PATH = "/sw.js";

/** What this browser offers for Web Push. */
export function browserFacts(): BrowserFacts {
  const nav = navigator as Navigator & { standalone?: boolean };
  return {
    hasServiceWorker: "serviceWorker" in nav,
    hasPushManager: "PushManager" in window,
    hasNotification: "Notification" in window,
    isIos: /iPad|iPhone|iPod/.test(nav.userAgent) || (nav.platform === "MacIntel" && nav.maxTouchPoints > 1),
    isStandalone: nav.standalone === true || window.matchMedia?.("(display-mode: standalone)").matches === true,
  };
}

export function notificationPermission(): NotificationPermissionState {
  return Notification.permission;
}

/** The push subscription this browser holds for the web app, or null without one. */
export async function currentSubscription(): Promise<PushSubscription | null> {
  const registration = await navigator.serviceWorker.getRegistration();
  return registration ? registration.pushManager.getSubscription() : null;
}

const sameKey = (key: ArrayBuffer | null | undefined, bytes: Uint8Array) =>
  key != null && key.byteLength === bytes.length && new Uint8Array(key).every((value, index) => value === bytes[index]);

/**
 * Asks the browser for the permission to show notifications, which the member agreed to before,
 * registers the service worker and subscribes to the push service with the VAPID key of the
 * server. The permission prompt comes first so it still belongs to the click that started this.
 */
export async function enableInBrowser(publicKey: string): Promise<PushSubscription> {
  if ((await Notification.requestPermission()) !== "granted") throw new PermissionDeniedError();
  await navigator.serviceWorker.register(WORKER_PATH);
  const { pushManager } = await navigator.serviceWorker.ready;
  const key = urlBase64ToBytes(publicKey);
  const existing = await pushManager.getSubscription();
  if (existing && sameKey(existing.options.applicationServerKey, key)) return existing;
  await existing?.unsubscribe();
  return pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
}

/** Stores the settings that the service worker reads when it shows a notification. */
export async function storeWorkerSettings(settings: WorkerSettings): Promise<void> {
  const cache = await caches.open(WORKER_SETTINGS_CACHE);
  await cache.put(WORKER_SETTINGS_URL, new Response(JSON.stringify(settings), { headers: { "Content-Type": "application/json" } }));
}

export async function clearWorkerSettings(): Promise<void> {
  await caches.delete(WORKER_SETTINGS_CACHE);
}
