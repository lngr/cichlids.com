import { describe, expect, it } from "vitest";
import { TRANSLATIONS } from "../i18n/catalogues";
import { SUPPORTED_LANGUAGES } from "../i18n/locales";
import { API_ERROR_CODES } from "../lib/apiErrors";
import { isTimeFixed } from "./careTime";

describe("the time of a care entry", () => {
  it("is fixed for an entry of the guided water change and free for every other entry", () => {
    expect(isTimeFixed({ waterChangeSessionId: 12 })).toBe(true);
    expect(isTimeFixed({ waterChangeSessionId: null })).toBe(false);
    expect(isTimeFixed({})).toBe(false);
  });

  it("has the note and the refusal in every language", () => {
    expect(API_ERROR_CODES).toContain("care_time_fixed");
    for (const language of SUPPORTED_LANGUAGES) {
      const catalogue = TRANSLATIONS[language] as unknown as { care: { when: Record<string, string> }; errors: Record<string, string> };
      expect(catalogue.care.when.fixed, `${language} fixed`).toBeTruthy();
      expect(catalogue.errors.care_time_fixed, `${language} care_time_fixed`).toBeTruthy();
    }
  });
});
