import { describe, expect, it } from "vitest";
import { permissionPrompt } from "./permissionPrompt";

describe("permissionPrompt", () => {
  const base = { supported: true, remindersEnabled: true, permission: "undetermined" as const, dismissed: false };
  it("explains the reminders before the system asks, once reminders are on", () => {
    expect(permissionPrompt(base)).toBe("explain");
  });
  it("stays quiet after the member postponed the explanation", () => {
    expect(permissionPrompt({ ...base, dismissed: true })).toBe("none");
  });
  it("shows a calm hint with the way to the settings when the permission is denied", () => {
    expect(permissionPrompt({ ...base, permission: "denied" })).toBe("denied");
    expect(permissionPrompt({ ...base, permission: "denied", dismissed: true })).toBe("denied");
  });
  it("shows nothing when reminders are off, the permission is granted or the platform cannot schedule", () => {
    expect(permissionPrompt({ ...base, remindersEnabled: false })).toBe("none");
    expect(permissionPrompt({ ...base, remindersEnabled: false, permission: "denied" })).toBe("none");
    expect(permissionPrompt({ ...base, permission: "granted" })).toBe("none");
    expect(permissionPrompt({ ...base, supported: false })).toBe("none");
    expect(permissionPrompt({ ...base, supported: false, permission: "denied" })).toBe("none");
  });
});
