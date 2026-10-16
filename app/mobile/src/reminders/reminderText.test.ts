import { describe, expect, it } from "vitest";
import i18next from "i18next";
import en from "../i18n/en.json";
import de from "../i18n/de.json";
import pl from "../i18n/pl.json";
import { reminderBody } from "./reminderText";

const translator = async (lng: string, translation: object) => {
  const instance = i18next.createInstance();
  await instance.init({ lng, fallbackLng: false, resources: { [lng]: { translation } } });
  return instance.t.bind(instance) as (key: string, options?: { count: number }) => string;
};

describe("reminderBody", () => {
  it("names the day of the care for a reminder outside a running streak", async () => {
    const t = await translator("en", en);
    expect(reminderBody({ kind: "water_change", daysLeft: null }, t)).toBe("A water change is due today.");
    expect(reminderBody({ kind: "measurement", daysLeft: null }, t)).toBe("A water measurement is due today.");
  });

  it("says in how many days the streak breaks and suggests a vacation", async () => {
    const t = await translator("en", en);
    expect(reminderBody({ kind: "water_change", daysLeft: 3 }, t)).toBe(
      "Your water change streak breaks in 3 days. Do the water change or enter a vacation if you are away.");
    expect(reminderBody({ kind: "water_change", daysLeft: 1 }, t)).toContain("breaks in 1 day.");
  });

  it("says that the streak breaks today on its last day", async () => {
    const t = await translator("en", en);
    expect(reminderBody({ kind: "water_change", daysLeft: 0 }, t)).toBe(
      "Your water change streak breaks today. Do the water change now or enter a vacation if you are away.");
  });

  it("uses the plural forms of the language", async () => {
    expect(reminderBody({ kind: "water_change", daysLeft: 2 }, await translator("pl", pl))).toContain("za 2 dni");
    expect(reminderBody({ kind: "water_change", daysLeft: 1 }, await translator("de", de))).toContain("in 1 Tag ab");
    expect(reminderBody({ kind: "water_change", daysLeft: 3 }, await translator("de", de))).toContain("in 3 Tagen ab");
  });
});
