type BrowserSubscription = { endpoint: string; unsubscribe(): Promise<boolean> };

/**
 * Deletes the Web Push subscription of this browser on the server and in the browser, and forgets
 * the settings of the service worker and the subscribing account. The browser side is always
 * cleaned up, also when the server cannot be reached: a subscription the browser no longer holds
 * answers the next message with 410, and the server then deletes it.
 */
export async function switchOffWith(deps: {
  current: () => Promise<BrowserSubscription | null>;
  deleteOnServer: (endpoint: string) => Promise<unknown>;
  clearSettings: () => Promise<void>;
  forgetOwner: () => void;
}): Promise<void> {
  const subscription = await deps.current();
  try {
    if (subscription) await deps.deleteOnServer(subscription.endpoint).catch(() => {});
  } finally {
    try {
      if (subscription) await subscription.unsubscribe();
      await deps.clearSettings();
    } finally {
      deps.forgetOwner();
    }
  }
}
