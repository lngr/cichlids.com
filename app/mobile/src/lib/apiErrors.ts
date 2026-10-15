import { ApiError } from "@cichlids/client-core";

/** The error codes the API returns in the code field of its problem details. */
export const API_ERROR_CODES = [
  "care_not_found",
  "care_time_invalid",
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
  "self_rating",
  "non_member_profile",
  "comment_too_long",
  "draft_limit_reached",
  "upload_rate_limited",
  "request_too_large",
  "tank_title_invalid",
  "tank_not_found",
  "discover_invalid_regions",
  "today_invalid_date",
  "tank_dimensions_invalid",
  "tank_volume_invalid",
  "tank_region_invalid",
  "onboarding_invalid",
  "interests_invalid",
  "legacy_tank_not_found",
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
