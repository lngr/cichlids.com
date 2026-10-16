import { useEffect } from "react";
import { AppState } from "react-native";
import { apiClient } from "../api/client";
import { useAuth } from "../auth/AuthProvider";
import { useNavigationAdapter } from "../navigation/NavigationProvider";
import { localTime } from "../today/localDate";
import { clearReminders, remindersSupported, startReminderListeners, syncReminders } from "./platform";
import { reminderRefresh } from "./refreshSignal";

/**
 * Keeps the local care reminders of the device in line with the due care of the signed-in
 * member: it recomputes them at start, when the app returns to the foreground and when a screen
 * requests a refresh, and it removes them when the member signs out. A tap on a reminder opens
 * the care view of its tank. It renders nothing, and does nothing where the platform cannot
 * schedule local notifications.
 */
export function RemindersHost() {
  const { status } = useAuth();
  const adapter = useNavigationAdapter();
  useEffect(() => {
    if (!remindersSupported) return;
    return startReminderListeners(path => adapter.current?.external(path));
  }, [adapter]);
  useEffect(() => {
    if (!remindersSupported || status === "loading") return;
    let queue: Promise<void> = Promise.resolve();
    const enqueue = (job: () => Promise<void>) => { queue = queue.then(job).catch(() => {}); };
    if (status === "anonymous") {
      enqueue(clearReminders);
      return;
    }
    const refresh = () => enqueue(async () => syncReminders(await apiClient.care.due(localTime())));
    refresh();
    const foreground = AppState.addEventListener("change", state => { if (state === "active") refresh(); });
    const unsubscribe = reminderRefresh.subscribe(refresh);
    return () => { foreground.remove(); unsubscribe(); };
  }, [status]);
  return null;
}
