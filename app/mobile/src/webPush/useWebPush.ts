import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import type { WebPushConfig } from "@cichlids/client-core";
import { API_BASE_URL, apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { translateApiError } from "../lib/apiErrors";
import { existingSubscriptionAction, readOwner, rememberOwner } from "./ownership";
import { browserFacts, clearWorkerSettings, currentSubscription, enableInBrowser, notificationPermission, storeWorkerSettings } from "./pushBrowser";
import { panelState, PermissionDeniedError, webPushSupport, type NotificationPermissionState, type PanelState } from "./support";
import { switchOffWebPush } from "./switchOff";
import { subscriptionRequest, workerSettings } from "./workerSettings";

const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;

/**
 * The reminder setting of this browser. Reminders are off until the member switches them on, agrees
 * to the explanation of the push service, and allows notifications in the browser; switching them
 * off deletes the subscription in the browser and on the server. A subscription that exists already
 * is posted to the server when the setting opens, which keeps the server in step with the browser,
 * but only for the account that subscribed in this browser; a subscription of another account is
 * removed from the browser, so no member receives reminders without their own consent.
 */
export function useWebPush() {
  const { t, i18n } = useTranslation();
  const { accountId } = useAuth();
  const support = useMemo(() => webPushSupport(browserFacts()), []);
  const [config, setConfig] = useState<WebPushConfig | null>(null);
  const [permission, setPermission] = useState<NotificationPermissionState>(() => support === "supported" ? notificationPermission() : "default");
  const [subscribed, setSubscribed] = useState(false);
  const [explaining, setExplaining] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (support !== "supported") return;
    let cancelled = false;
    void (async () => {
      try {
        const loaded = await apiClient.webPush.config();
        if (cancelled) return;
        setConfig(loaded);
        const existing = await currentSubscription();
        const action = existingSubscriptionAction({
          hasSubscription: existing !== null, owner: readOwner(), accountId: accountId ?? null,
          permission: notificationPermission(), serverAvailable: loaded.available,
        });
        if (existing && action === "resync") {
          await apiClient.webPush.subscribe(subscriptionRequest(existing.toJSON(), timeZone()));
          await storeWorkerSettings(workerSettings(API_BASE_URL, t));
          if (!cancelled) setSubscribed(true);
        } else if (existing && action === "discard") {
          await existing.unsubscribe();
          await clearWorkerSettings();
        }
      } catch {
        if (!cancelled) setConfig(current => current ?? { available: false, publicKey: null });
      }
    })();
    return () => { cancelled = true; };
    // The texts for the worker are refreshed by the language effect below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [support, accountId]);

  useEffect(() => {
    if (subscribed) void storeWorkerSettings(workerSettings(API_BASE_URL, t)).catch(() => {});
  }, [subscribed, i18n.language, t]);

  const state: PanelState = panelState({ support, serverAvailable: config?.available ?? null, permission, subscribed, explaining });

  const switchOn = useCallback(() => { setError(null); setExplaining(true); }, []);
  const cancel = useCallback(() => setExplaining(false), []);

  const agree = useCallback(async () => {
    if (!config?.publicKey) return;
    setExplaining(false);
    setBusy(true);
    setError(null);
    try {
      const subscription = await enableInBrowser(config.publicKey);
      setPermission(notificationPermission());
      await apiClient.webPush.subscribe(subscriptionRequest(subscription.toJSON(), timeZone()));
      if (accountId) rememberOwner(accountId);
      await storeWorkerSettings(workerSettings(API_BASE_URL, t));
      setSubscribed(true);
    } catch (failure) {
      setPermission(notificationPermission());
      if (!(failure instanceof PermissionDeniedError)) {
        setError(translateApiError(failure, t, "webPush.failed"));
        await (await currentSubscription())?.unsubscribe().catch(() => {});
      }
    } finally {
      setBusy(false);
    }
  }, [config, t, accountId]);

  const switchOff = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      await switchOffWebPush();
      setSubscribed(false);
    } catch (failure) {
      setError(translateApiError(failure, t, "webPush.failed"));
    } finally {
      setBusy(false);
    }
  }, [t]);

  return { state, busy, error, switchOn, cancel, agree, switchOff };
}
