export type NotificationPermission = "granted" | "denied" | "undetermined";

export type PermissionPrompt = "none" | "explain" | "denied";

/**
 * What the care view shows about the notification permission: an explanation before the system
 * asks (permission not yet decided and not postponed), a calm hint with the way to the system
 * settings (permission denied), or nothing. Only a platform that schedules reminders and a tank
 * with reminders on show anything.
 */
export function permissionPrompt({ supported, remindersEnabled, permission, dismissed }: {
  supported: boolean; remindersEnabled: boolean; permission: NotificationPermission; dismissed: boolean;
}): PermissionPrompt {
  if (!supported || !remindersEnabled) return "none";
  if (permission === "denied") return "denied";
  return permission === "undetermined" && !dismissed ? "explain" : "none";
}
