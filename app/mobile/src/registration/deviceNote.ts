export const DEVICE_NOTE_KEY = "cichlids.device.noteSeen";

export interface NoteStorage {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
}

/** Whether the note that the account lives on this device was shown before the first tank. */
export async function isDeviceNoteSeen(storage: NoteStorage): Promise<boolean> {
  try {
    return (await storage.getItem(DEVICE_NOTE_KEY)) === "1";
  } catch {
    return false;
  }
}

/** Remembers that the note was shown. A storage that fails keeps nothing, and the note shows at the next start. */
export async function rememberDeviceNote(storage: NoteStorage): Promise<void> {
  try {
    await storage.setItem(DEVICE_NOTE_KEY, "1");
  } catch {
    // The note shows at the next start.
  }
}

/**
 * Whether the app leads the member to the first tank on its own. A registered account goes by the
 * onboarding it finished; a provisional account cannot finish it and is led there once, together
 * with the note about the device account.
 */
export function shouldOfferFirstTank(input: { provisional: boolean; onboardingCompleted: boolean; noteSeen: boolean }): boolean {
  if (input.provisional) return !input.onboardingCompleted && !input.noteSeen;
  return !input.onboardingCompleted;
}
