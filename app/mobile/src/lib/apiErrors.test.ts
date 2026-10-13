import { ApiError } from "@cichlids/client-core";
import { describe, expect, it } from "vitest";
import de from "../i18n/de.json";
import en from "../i18n/en.json";
import { API_ERROR_CODES, apiErrorKey, translateApiError } from "./apiErrors";

describe("apiErrorKey", () => {
  it("maps a known API error code to its translation key", () => {
    expect(apiErrorKey(Object.assign(new Error("Forbidden"), { status: 403, code: "self_rating" }))).toBe("errors.self_rating");
    expect(apiErrorKey({ code: "non_member_profile" })).toBe("errors.non_member_profile");
    expect(apiErrorKey({ code: "comment_too_long" })).toBe("errors.comment_too_long");
    expect(apiErrorKey({ code: "draft_limit_reached" })).toBe("errors.draft_limit_reached");
    expect(apiErrorKey({ code: "upload_rate_limited" })).toBe("errors.upload_rate_limited");
    expect(apiErrorKey({ code: "request_too_large" })).toBe("errors.request_too_large");
  });

  it("returns null for unknown codes, missing codes and non-objects", () => {
    expect(apiErrorKey({ code: "something_new" })).toBeNull();
    expect(apiErrorKey(new Error("Failed to fetch"))).toBeNull();
    expect(apiErrorKey({ code: 5 })).toBeNull();
    expect(apiErrorKey("self_rating")).toBeNull();
    expect(apiErrorKey(null)).toBeNull();
  });
});

describe("apiErrorKey for connection failures", () => {
  it("maps a call that got no answer to the connection message", () => {
    expect(apiErrorKey(new ApiError("Network error", 0))).toBe("errors.connection");
  });

  it("maps an unreachable identity provider behind the API to the connection message", () => {
    expect(apiErrorKey(new ApiError("Service Unavailable", 503, "identity_provider_unavailable"))).toBe("errors.connection");
  });

  it("leaves answered calls without a known code alone", () => {
    expect(apiErrorKey(new ApiError("Unauthorized", 401))).toBeNull();
    expect(apiErrorKey(new ApiError("Bad Gateway", 502))).toBeNull();
  });
});

describe("translateApiError", () => {
  const t = (key: string) => `translated:${key}`;

  it("translates a known API error code", () => {
    expect(translateApiError({ code: "self_rating" }, t, "common.loadError")).toBe("translated:errors.self_rating");
  });

  it("translates the code of an ApiError", () => {
    expect(translateApiError(new ApiError("You cannot rate your own picture.", 403, "self_rating"), t, "common.loadError")).toBe(
      "translated:errors.self_rating",
    );
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
    expect(errors.connection, "connection").toBeTruthy();
  });
});
