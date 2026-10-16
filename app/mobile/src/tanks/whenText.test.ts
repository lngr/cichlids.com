import { describe, expect, it } from "vitest";
import { TRANSLATIONS } from "../i18n/catalogues";
import { SUPPORTED_LANGUAGES } from "../i18n/locales";

const KEYS = ["title", "waterChangeTitle", "now", "keep", "yesterday", "twoDaysAgo", "custom", "date", "time", "dateLabel", "timeLabel", "datePlaceholder", "timePlaceholder",
  "errorFuture", "errorInvalid", "preview", "current", "save"] as const;

describe("texts of the time choice", () => {
  it("has every text in every language, with the time placeholder where a time is shown", () => {
    for (const language of SUPPORTED_LANGUAGES) {
      const when = (TRANSLATIONS[language].care as unknown as { when: Record<string, string> }).when;
      for (const key of KEYS) expect(when[key], `${language} ${key}`).toBeTruthy();
      expect(when.preview, `${language} preview`).toContain("{{time}}");
      expect(when.current, `${language} current`).toContain("{{time}}");
    }
  });
});
