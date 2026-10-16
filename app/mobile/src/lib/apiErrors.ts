import { ApiError } from "@cichlids/client-core";

/** The error codes the API returns in the code field of its problem details. */
export const API_ERROR_CODES = [
  "care_not_found",
  "care_time_invalid",
  "care_time_fixed",
  "care_kind_invalid",
  "care_amount_invalid",
  "care_request_invalid",
  "care_auth_required",
  "care_measurement_invalid",
  "care_parameter_invalid",
  "care_unit_invalid",
  "care_value_invalid",
  "care_origin_invalid",
  "care_range_invalid",
  "care_plan_invalid",
  "care_note_invalid",
  "care_photo_not_found",
  "care_photo_invalid",
  "care_photo_limit_reached",
  "todo_not_found",
  "todo_text_invalid",
  "todo_request_invalid",
  "care_date_invalid",
  "self_rating",
  "non_member_profile",
  "comment_too_long",
  "draft_limit_reached",
  "upload_rate_limited",
  "tank_photo_invalid",
  "tank_photo_auth_required",
  "request_too_large",
  "tank_title_invalid",
  "tank_not_found",
  "tank_visibility_answer_required",
  "tank_visibility_invalid",
  "tank_stream_wider_than_profile",
  "tank_media_not_found",
  "discover_invalid_regions",
  "today_invalid_date",
  "tank_dimensions_invalid",
  "tank_volume_invalid",
  "tank_region_invalid",
  "onboarding_invalid",
  "interests_invalid",
  "legacy_tank_not_found",
  "web_push_unavailable",
  "web_push_limit_reached",
  "web_push_endpoint_not_allowed",
  "web_push_subscription_invalid",
  "web_push_request_invalid",
  "web_push_auth_required",
  "web_push_snooze_invalid",
  "video_upload_not_found",
  "video_upload_invalid",
  "video_upload_offset_mismatch",
  "video_upload_complete",
  "video_upload_incomplete",
  "video_upload_expired",
  "video_upload_limit_reached",
  "video_chunk_too_large",
  "video_chunk_incomplete",
  "video_format_unsupported",
  "video_too_large",
  "video_target_not_found",
  "video_not_found",
  "care_video_limit_reached",
  "video_request_invalid",
  "video_auth_required",
  "video_too_long",
  "video_unreadable",
  "video_no_video_stream",
  "video_codec_unsupported",
  "video_dimensions_unsupported",
  "video_processing_failed",
  "care_video_not_found",
  "water_change_not_found",
  "water_change_request_invalid",
  "streak_not_found",
  "streak_date_invalid",
  "streak_request_invalid",
  "streak_auth_required",
  "vacation_not_found",
  "vacation_dates_invalid",
  "vacation_active",
  "vacation_overlap",
  "vacation_would_repair",
  "decommission_photo_invalid",
  "decommission_photo_not_in_app",
  "decommission_photo_time",
  "decommission_photo_reused",
  "decommission_scope_invalid",
  "decommission_active",
  "decommission_not_active",
  "decommission_date_invalid",
  "decommission_request_invalid",
  "decommission_auth_required",
] as const;

export type ApiErrorCode = (typeof API_ERROR_CODES)[number];

/** The problem code of a 503 answer when the API cannot reach the identity provider to renew a web session. */
const IDENTITY_PROVIDER_UNAVAILABLE = "identity_provider_unavailable";

/**
 * The translation key for an error: the connection message for a call that got no answer (an
 * ApiError with status 0, from a network failure or a session that could not be renewed) or that
 * the API could not serve because the identity provider was unreachable; otherwise the key of the
 * API error code the error carries, or null when the error has no code or a code the app does not
 * know.
 */
export function apiErrorKey(error: unknown): `errors.${ApiErrorCode}` | "errors.connection" | null {
  if (typeof error !== "object" || error === null) return null;
  const code = (error as { code?: unknown }).code;
  if ((error instanceof ApiError && error.status === 0) || code === IDENTITY_PROVIDER_UNAVAILABLE) return "errors.connection";
  if (typeof code !== "string") return null;
  return (API_ERROR_CODES as readonly string[]).includes(code) ? (`errors.${code}` as `errors.${ApiErrorCode}`) : null;
}

/**
 * The text for a failed API call: the translation of the API error code the error carries, else
 * the translation of the given fallback key. Technical messages of the error are not shown.
 */
export function translateApiError(error: unknown, t: (key: string) => string, fallbackKey: string): string {
  return t(apiErrorKey(error) ?? fallbackKey);
}
