import { useCallback, useEffect, useState } from "react";
import { AppState } from "react-native";
import { openSystemSettings, readPermission, remindersSupported, requestPermission } from "./platform";
import { permissionPrompt, type NotificationPermission, type PermissionPrompt } from "./permissionPrompt";
import { reminderRefresh } from "./refreshSignal";

// A postponed explanation stays hidden until the app restarts.
let explanationPostponed = false;

/**
 * The permission side of the reminders in the care view: what to show about it, and the actions
 * to allow reminders (after the explanation), to postpone the explanation and to open the system
 * settings. The system dialog only opens from the allow action.
 */
export function useReminderPermission(remindersEnabled: boolean) {
  const [permission, setPermission] = useState<NotificationPermission | null>(null);
  const [dismissed, setDismissed] = useState(explanationPostponed);
  useEffect(() => {
    if (!remindersSupported) return;
    let cancelled = false;
    const read = () => void readPermission().then(value => { if (!cancelled) setPermission(value); }).catch(() => {});
    read();
    const subscription = AppState.addEventListener("change", state => { if (state === "active") read(); });
    return () => { cancelled = true; subscription.remove(); };
  }, []);
  const allow = useCallback(async () => {
    const result = await requestPermission();
    setPermission(result);
    if (result === "granted") reminderRefresh.request();
  }, []);
  const postpone = useCallback(() => { explanationPostponed = true; setDismissed(true); }, []);
  const prompt: PermissionPrompt = permission === null ? "none"
    : permissionPrompt({ supported: remindersSupported, remindersEnabled, permission, dismissed });
  return { prompt, allow, postpone, openSettings: openSystemSettings };
}
