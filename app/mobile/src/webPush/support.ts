/** What the browser offers for Web Push, read from its globals. */
export type BrowserFacts = {
  hasServiceWorker: boolean;
  hasPushManager: boolean;
  hasNotification: boolean;
  isIos: boolean;
  /** True when the page runs as a web app opened from the home screen. */
  isStandalone: boolean;
};

export type WebPushSupport = "supported" | "unsupported" | "needs_home_screen";

/**
 * Whether the browser can receive Web Push. iOS and iPadOS deliver it only to a web app added to
 * the home screen, so a page in a normal Safari tab is asked to be added first.
 */
export function webPushSupport(facts: BrowserFacts): WebPushSupport {
  if (facts.isIos && !facts.isStandalone) return "needs_home_screen";
  return facts.hasServiceWorker && facts.hasPushManager && facts.hasNotification ? "supported" : "unsupported";
}

export type NotificationPermissionState = "default" | "granted" | "denied";

export type PanelState = "loading" | "unsupported" | "needs_home_screen" | "unavailable" | "off" | "explain" | "denied" | "on";

/**
 * What the reminder setting of the browser shows. The setting is off until the member switches it
 * on; the explanation of the push service appears first and only the agreement leads to the
 * permission prompt of the browser.
 */
export function panelState({ support, serverAvailable, permission, subscribed, explaining }: {
  support: WebPushSupport;
  /** Whether the server sends browser notifications; null until the server has answered. */
  serverAvailable: boolean | null;
  permission: NotificationPermissionState;
  subscribed: boolean;
  explaining: boolean;
}): PanelState {
  if (support !== "supported") return support;
  if (serverAvailable === null) return "loading";
  if (!serverAvailable) return "unavailable";
  if (permission === "denied") return "denied";
  if (explaining) return "explain";
  return subscribed && permission === "granted" ? "on" : "off";
}

/** The member declined, or did not answer, the permission prompt of the browser. */
export class PermissionDeniedError extends Error {
  constructor() {
    super("The browser did not grant the permission for notifications.");
  }
}
