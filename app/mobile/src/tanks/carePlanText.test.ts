import i18next, { type TFunction } from "i18next";
import { beforeAll, describe, expect, it } from "vitest";
import type { CarePlanRule } from "@cichlids/client-core";
import { TRANSLATIONS } from "../i18n/catalogues";
import { SUPPORTED_LANGUAGES } from "../i18n/locales";
import { dueNote, dueText, ruleText } from "./carePlanText";

const rule = (overrides: Partial<CarePlanRule>): CarePlanRule => ({
  kind: "water_change", rhythmDays: 7, enabled: true, isDefault: true,
  reason: { code: "cichlid_default", parameters: null }, due: null, ...overrides,
});
let en: TFunction;
let de: TFunction;
beforeAll(async () => {
  en = (await i18next.createInstance().init({ lng: "en", resources: { en: { translation: TRANSLATIONS.en } } })) as TFunction;
  de = (await i18next.createInstance().init({ lng: "de", resources: { de: { translation: TRANSLATIONS.de } } })) as TFunction;
});

describe("care plan wording", () => {
  it("states the rhythm with the reason of the rule", () => {
    expect(ruleText(rule({}), en)).toBe("Every 7 days: The usual recommendation for cichlid tanks");
    expect(ruleText(rule({ rhythmDays: 1 }), en)).toBe("Every day: The usual recommendation for cichlid tanks");
    expect(ruleText(rule({ rhythmDays: 14, isDefault: false, reason: { code: "member_choice", parameters: null } }), en)).toBe("Every 14 days: Your choice");
    expect(ruleText(rule({}), de)).toBe("Alle 7 Tage: Die übliche Empfehlung für Buntbarsch-Becken");
  });
  it("falls back to a generic reason for a code the app does not know", () => {
    expect(ruleText(rule({ reason: { code: "stocking_mbuna", parameters: { count: "12", volume: "240" } } }), en)).toBe("Every 7 days: Set in your care plan");
  });
  it("has no text for a rule without a rhythm", () => {
    expect(ruleText(rule({ rhythmDays: null, reason: null }), en)).toBeNull();
  });
  it("describes every due state calmly", () => {
    expect(dueText({ state: "due_in", days: 3, dueDate: "2026-10-18" }, en)).toBe("Next in 3 days");
    expect(dueText({ state: "due_in", days: 1, dueDate: "2026-10-16" }, en)).toBe("Next in 1 day");
    expect(dueText({ state: "due_today", days: 0, dueDate: "2026-10-15" }, en)).toBe("Due today");
    expect(dueText({ state: "overdue", days: 3, dueDate: "2026-10-12" }, en)).toBe("Planned 3 days ago");
    expect(dueText({ state: "never_logged", days: 0, dueDate: null }, en)).toBe("Not logged yet");
    expect(dueNote({ state: "overdue", days: 3, dueDate: "2026-10-12" }, en)).toBe("No rush, do it when it suits you.");
    expect(dueNote({ state: "due_today", days: 0, dueDate: "2026-10-15" }, en)).toBeNull();
  });
  it("has the care plan texts in every language", () => {
    for (const language of SUPPORTED_LANGUAGES) {
      const plan = TRANSLATIONS[language].care.plan as Record<string, unknown>;
      for (const key of ["title", "daily", "dueToday", "neverLogged", "overdueNote", "rhythmReason", "reasonOther", "switchedOff", "noRule", "edit", "off", "on", "add", "customLabel", "saveCustom", "close"]) {
        expect(plan[key], `${language} ${key}`).toBeTruthy();
      }
      expect((plan.kind as Record<string, string>).water_change, language).toBeTruthy();
      expect((plan.kind as Record<string, string>).measurement, language).toBeTruthy();
      for (const code of ["cichlid_default", "member_choice"]) expect((plan.reason as Record<string, string>)[code], `${language} ${code}`).toBeTruthy();
    }
  });
});
