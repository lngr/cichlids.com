/** The error codes the API returns in the code field of its problem details. */
export const API_ERROR_CODES = [
  "self_rating",
  "non_member_profile",
  "comment_too_long",
  "draft_limit_reached",
  "upload_rate_limited",
  "request_too_large",
] as const;

export type ApiErrorCode = (typeof API_ERROR_CODES)[number];

/**
 * The translation key for the API error code an error carries, or null when the error has no
 * code or a code the app does not know.
 */
export function apiErrorKey(error: unknown): `errors.${ApiErrorCode}` | null {
  if (typeof error !== "object" || error === null) return null;
  const code = (error as { code?: unknown }).code;
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
