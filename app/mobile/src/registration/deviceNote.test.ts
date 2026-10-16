import { describe, expect, it } from "vitest";
import { DEVICE_NOTE_KEY, isDeviceNoteSeen, rememberDeviceNote, shouldOfferFirstTank, type NoteStorage } from "./deviceNote";

function memory(): NoteStorage & { values: Map<string, string> } {
  const values = new Map<string, string>();
  return {
    values,
    async getItem(key) { return values.get(key) ?? null; },
    async setItem(key, value) { values.set(key, value); },
  };
}

describe("the note about the device account", () => {
  it("is unseen on a fresh installation", async () => {
    expect(await isDeviceNoteSeen(memory())).toBe(false);
  });

  it("is remembered once shown", async () => {
    const storage = memory();
    await rememberDeviceNote(storage);
    expect(storage.values.get(DEVICE_NOTE_KEY)).toBe("1");
    expect(await isDeviceNoteSeen(storage)).toBe(true);
  });

  it("counts as unseen when the storage fails", async () => {
    const broken: NoteStorage = { getItem: async () => { throw new Error("denied"); }, setItem: async () => { throw new Error("denied"); } };
    expect(await isDeviceNoteSeen(broken)).toBe(false);
    await expect(rememberDeviceNote(broken)).resolves.toBeUndefined();
  });
});

describe("shouldOfferFirstTank", () => {
  it("leads a provisional account that has not seen the note and never finished onboarding to the first tank", () => {
    expect(shouldOfferFirstTank({ provisional: true, onboardingCompleted: false, noteSeen: false })).toBe(true);
  });

  it("does not lead it there twice", () => {
    expect(shouldOfferFirstTank({ provisional: true, onboardingCompleted: false, noteSeen: true })).toBe(false);
  });

  it("follows the onboarding state for a registered account", () => {
    expect(shouldOfferFirstTank({ provisional: false, onboardingCompleted: false, noteSeen: true })).toBe(true);
    expect(shouldOfferFirstTank({ provisional: false, onboardingCompleted: true, noteSeen: false })).toBe(false);
  });
});
