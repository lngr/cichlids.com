import { expect, it } from "vitest";
import { amountDraft, parseCareAmount, formatCareAmount } from "./careAmount";

it("defaults input to the member's units and accepts localized decimals", () => {
  expect(amountDraft(null, "us")).toEqual({ value: "", unit: "us_gallon" });
  expect(amountDraft(null, "metric")).toEqual({ value: "", unit: "liter" });
  expect(parseCareAmount("12,5", "liter")).toEqual({ value: 12.5, unit: "liter" });
  for (const value of ["", "0", "-1", "NaN", "Infinity", "1e3", "1,2,3"]) expect(parseCareAmount(value, "liter")).toBeNull();
  expect(parseCareAmount("101", "percent")).toBeNull();
});
it("preserves stored input units when editing and displaying", () => {
  const amount = { value: 55, unit: "us_gallon" as const, liters: 208.19764812 };
  expect(amountDraft(amount, "metric")).toEqual({ value: "55", unit: "us_gallon" });
  expect(formatCareAmount(amount, "en")).toBe("55 US gal");
  expect(formatCareAmount({ value: 12.5, unit: "percent", liters: null }, "de")).toBe("12,5 %");
});
