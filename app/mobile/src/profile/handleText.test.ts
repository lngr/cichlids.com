import i18next, { type TFunction } from "i18next";
import { ApiError } from "@cichlids/client-core";
import { beforeAll, describe, expect, it } from "vitest";
import { TRANSLATIONS } from "../i18n/catalogues";
import { apiErrorKey } from "../lib/apiErrors";
import { canSubmitHandle, handleErrorText, nextChangeText } from "./handleText";

let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

describe("handleErrorText", () => {
  it.each(["handle_invalid", "handle_generated", "handle_reserved", "handle_taken", "handle_change_too_soon"])("translates %s in every language", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it("names the reason of a refused handle", () => {
    expect(handleErrorText(new ApiError("x", 409, "handle_taken"), en, "en")).toBe("This handle is already in use or reserved for another member.");
    expect(handleErrorText(new ApiError("x", 409, "handle_taken"), de, "de")).toBe("Dieser Name ist schon vergeben oder für ein anderes Mitglied reserviert.");
  });

  it("names the date of the next change when the handle changed too recently", () => {
    const error = new ApiError("x", 429, "handle_change_too_soon", { nextChangeAt: "2026-11-16T12:00:00+00:00" });
    expect(handleErrorText(error, en, "en")).toBe("You changed your handle recently. The next change is possible on Nov 16, 2026.");
    expect(handleErrorText(error, de, "de")).toBe("Du hast deinen Namen kürzlich geändert. Die nächste Änderung ist am 16. Nov. 2026 möglich.");
  });

  it("falls back to the general text for other failures", () => {
    expect(handleErrorText(new ApiError("x", 500), en, "en")).toBe(en("common.loadError"));
    expect(handleErrorText(new ApiError("x", 429, "handle_change_too_soon"), en, "en")).toBe(
      "You changed your handle recently. Please try again later.");
  });
});

describe("nextChangeText", () => {
  const now = new Date("2026-10-17T10:00:00Z");
  it("names the day of the next possible change", () => {
    expect(nextChangeText("2026-11-16T12:00:00+00:00", now, en, "en")).toBe("You can change your handle again on Nov 16, 2026.");
  });
  it("says nothing when a change is possible", () => {
    expect(nextChangeText(null, now, en, "en")).toBeNull();
    expect(nextChangeText(undefined, now, en, "en")).toBeNull();
    expect(nextChangeText("2026-10-17T09:00:00+00:00", now, en, "en")).toBeNull();
    expect(nextChangeText("not a date", now, en, "en")).toBeNull();
  });
});

describe("canSubmitHandle", () => {
  it.each([
    ["anna", "bob", true],
    ["  anna ", "bob", true],
    ["", "bob", false],
    ["   ", "bob", false],
    ["bob", "bob", false],
    [" bob ", "bob", false],
  ] as const)("checks %j against %j", (input, current, expected) => {
    expect(canSubmitHandle(input, current)).toBe(expected);
  });
});
