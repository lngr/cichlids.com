import type { DueCareItem } from "@cichlids/client-core";
import type { NotificationPermission } from "./permissionPrompt";

/** False in the browser: local notifications are scheduled by the native app only. */
export const remindersSupported = false;

export async function readPermission(): Promise<NotificationPermission> {
  return "undetermined";
}

export async function requestPermission(): Promise<NotificationPermission> {
  return "undetermined";
}

export function openSystemSettings(): void {}

export async function syncReminders(_items: DueCareItem[], _now?: Date): Promise<void> {}

export async function clearReminders(): Promise<void> {}

export function startReminderListeners(_open: (path: string) => void): () => void {
  return () => {};
}
