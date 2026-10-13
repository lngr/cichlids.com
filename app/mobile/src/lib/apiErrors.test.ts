import { describe, expect, it } from "vitest";
import de from "../i18n/de.json";
import en from "../i18n/en.json";
import { API_ERROR_CODES, apiErrorKey, translateApiError } from "./apiErrors";

describe("apiErrorKey", () => {
  it("maps a known API error code to its translation key", () => {
    expect(apiErrorKey(Object.assign(new Error("Forbidden"), { status: 403, code: "self_rating" }))).toBe("errors.self_rating");
    expect(apiErrorKey({ code: "non_member_profile" })).toBe("errors.non_member_profile");
  });

  it("returns null for unknown codes, missing codes and non-objects", () => {
    expect(apiErrorKey({ code: "something_new" })).toBeNull();
    expect(apiErrorKey(new Error("Failed to fetch"))).toBeNull();
    expect(apiErrorKey({ code: 5 })).toBeNull();
    expect(apiErrorKey("self_rating")).toBeNull();
    expect(apiErrorKey(null)).toBeNull();
  });
});

describe("translateApiError", () => {
  const t = (key: string) => `translated:${key}`;

  it("translates a known API error code", () => {
    expect(translateApiError({ code: "self_rating" }, t, "common.loadError")).toBe("translated:errors.self_rating");
  });

  it("translates the fallback key for errors without a known code, never showing the raw message", () => {
    expect(translateApiError(new Error("Failed to fetch"), t, "common.loadError")).toBe("translated:common.loadError");
  });
});

describe("API error translations", () => {
  it.each([
    ["en", en],
    ["de", de],
  ])("%s translates every known code", (_name, resource) => {
    const errors = (resource as { errors?: Record<string, string> }).errors ?? {};
    for (const code of API_ERROR_CODES) {
      expect(errors[code], code).toBeTruthy();
    }
  });
});
