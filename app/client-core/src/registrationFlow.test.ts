import { describe, expect, it, vi } from "vitest";
import { ApiError } from "./client.js";
import type { CichlidsClient } from "./client.js";
import type { RegistrationMove, RegistrationPreview } from "./registration.js";
import {
  REGISTRATION_REASONS,
  completeRegistration,
  droppedVacations,
  hasProvisionalContent,
  isRegistrationRequired,
} from "./registrationFlow.js";

const local = { date: "2026-10-16", utcOffsetMinutes: 120 };
const content = { tanks: 2, careEntries: 14, mediaItems: 3, todos: 1, vacations: 1 };
const empty = { tanks: 0, careEntries: 0, mediaItems: 0, todos: 0, vacations: 0 };
const me = { profile: { id: 7 }, roles: [], provisional: false } as never;

function api(preview: Partial<RegistrationPreview>) {
  const full: RegistrationPreview = { mode: "upgrade", content, vacationsDropped: 0, vacationsDroppedRepair: 0, targetDisplayName: null, ...preview };
  const moved: RegistrationMove = { moved: full.content, vacationsDropped: full.vacationsDropped, vacationsDroppedRepair: full.vacationsDroppedRepair, me };
  return {
    preview: vi.fn(async () => full),
    upgrade: vi.fn(async () => me),
    move: vi.fn(async () => moved),
  } satisfies CichlidsClient["registration"];
}

describe("isRegistrationRequired", () => {
  it("recognises the answer for an action that needs a registered account", () => {
    expect(isRegistrationRequired(new ApiError("x", 403, "registration_required"))).toBe(true);
  });

  it("ignores other errors", () => {
    expect(isRegistrationRequired(new ApiError("x", 403, "account_suspended"))).toBe(false);
    expect(isRegistrationRequired(new ApiError("x", 401, "registration_required"))).toBe(false);
    expect(isRegistrationRequired(new Error("x"))).toBe(false);
    expect(isRegistrationRequired(null)).toBe(false);
  });
});

describe("REGISTRATION_REASONS", () => {
  it("names the generic reason that answers without a specific cause fall back to", () => {
    expect(REGISTRATION_REASONS).toContain("generic");
  });
});

describe("completeRegistration", () => {
  it("upgrades an account without a profile and does not ask", async () => {
    const registration = api({ mode: "upgrade" });
    const decide = vi.fn(async () => true);
    const outcome = await completeRegistration(registration, local, decide);
    expect(outcome).toEqual({ kind: "upgraded", me, content });
    expect(registration.upgrade).toHaveBeenCalledTimes(1);
    expect(registration.move).not.toHaveBeenCalled();
    expect(decide).not.toHaveBeenCalled();
  });

  it("asks before moving the content into an existing profile and moves on yes", async () => {
    const registration = api({ mode: "move", targetDisplayName: "Tanja", vacationsDropped: 1, vacationsDroppedRepair: 2 });
    const decide = vi.fn(async () => true);
    const outcome = await completeRegistration(registration, local, decide);
    expect(decide).toHaveBeenCalledWith(expect.objectContaining({ mode: "move", targetDisplayName: "Tanja" }));
    expect(registration.move).toHaveBeenCalledWith(local);
    expect(outcome.kind).toBe("moved");
    if (outcome.kind === "moved") {
      expect(outcome.moved).toEqual(content);
      expect(droppedVacations(outcome)).toBe(3);
    }
  });

  it("leaves the content on the device on no", async () => {
    const registration = api({ mode: "move" });
    const outcome = await completeRegistration(registration, local, async () => false);
    expect(outcome.kind).toBe("declined");
    expect(registration.move).not.toHaveBeenCalled();
    expect(registration.upgrade).not.toHaveBeenCalled();
  });

  it("moves an installation without content without asking", async () => {
    const registration = api({ mode: "move", content: empty });
    const decide = vi.fn(async () => false);
    const outcome = await completeRegistration(registration, local, decide);
    expect(outcome.kind).toBe("moved");
    expect(decide).not.toHaveBeenCalled();
  });

  it("passes failures of the API on", async () => {
    const registration = api({ mode: "upgrade" });
    registration.upgrade.mockRejectedValueOnce(new ApiError("x", 409, "account_has_profile"));
    await expect(completeRegistration(registration, local, async () => true)).rejects.toMatchObject({ code: "account_has_profile" });
  });
});

describe("hasProvisionalContent and droppedVacations", () => {
  it("counts tanks, care entries, media items, to-dos and vacations as content", () => {
    expect(hasProvisionalContent(empty)).toBe(false);
    expect(hasProvisionalContent({ ...empty, tanks: 1 })).toBe(true);
    expect(hasProvisionalContent({ ...empty, mediaItems: 1 })).toBe(true);
    expect(hasProvisionalContent({ ...empty, vacations: 2 })).toBe(true);
  });

  it("adds both kinds of dropped vacations", () => {
    expect(droppedVacations({ vacationsDropped: 1, vacationsDroppedRepair: 4 })).toBe(5);
  });
});
