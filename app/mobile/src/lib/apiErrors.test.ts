import { ApiError } from "@cichlids/client-core";
import { describe, expect, it } from "vitest";
import { TRANSLATIONS } from "../i18n/catalogues";
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

  it.each(["tank_title_invalid", "tank_dimensions_invalid", "tank_volume_invalid"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Bad Request", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it.each(["tank_photo_invalid", "tank_photo_auth_required"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it.each(["care_note_invalid", "care_photo_not_found", "care_photo_invalid", "care_photo_limit_reached", "todo_not_found", "todo_text_invalid", "todo_request_invalid"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it.each(["video_upload_not_found", "video_upload_invalid", "video_upload_offset_mismatch", "video_upload_complete", "video_upload_incomplete", "video_upload_expired", "video_upload_limit_reached", "video_chunk_too_large", "video_chunk_incomplete", "video_format_unsupported", "video_too_large", "video_target_not_found", "video_not_found", "care_video_limit_reached", "video_request_invalid", "video_auth_required", "video_too_long", "video_unreadable", "video_no_video_stream", "video_codec_unsupported", "video_dimensions_unsupported", "video_processing_failed", "care_video_not_found"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it.each(["web_push_unavailable", "web_push_limit_reached", "web_push_endpoint_not_allowed", "web_push_subscription_invalid", "web_push_request_invalid", "web_push_auth_required", "web_push_snooze_invalid"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
  });

  it.each(["decommission_photo_invalid", "decommission_photo_not_in_app", "decommission_photo_time", "decommission_photo_reused", "decommission_scope_invalid", "decommission_active", "decommission_not_active", "decommission_date_invalid", "decommission_request_invalid", "decommission_auth_required"])("translates %s", (code) => {
    expect(apiErrorKey(new ApiError("Refused", 400, code))).toBe(`errors.${code}`);
    for (const resource of Object.values(TRANSLATIONS)) {
      expect((resource.errors as Record<string, string>)[code], code).toBeTruthy();
    }
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
  it.each(Object.entries(TRANSLATIONS))("%s translates every known code", (_name, resource) => {
    const errors = (resource as { errors?: Record<string, string> }).errors ?? {};
    for (const code of API_ERROR_CODES) {
      expect(errors[code], code).toBeTruthy();
    }
    expect(errors.connection, "connection").toBeTruthy();
  });
});
