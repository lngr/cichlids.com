/** The tokens of one logged-in session; expiresAt is the access token's expiry in epoch milliseconds. */
export interface TokenSet {
  accessToken: string;
  refreshToken: string | null;
  idToken: string | null;
  expiresAt: number;
}

/** The token endpoint fields this module reads, as expo-auth-session's TokenResponse exposes them. */
export interface TokenEndpointResponse {
  accessToken: string;
  refreshToken?: string;
  idToken?: string;
  /** Lifetime of the access token in seconds. */
  expiresIn?: number;
}

/** An access token this close to its expiry is refreshed before use, so it cannot expire in flight. */
export const REFRESH_MARGIN_MS = 30_000;

export function needsRefresh(tokenSet: TokenSet, nowMs: number): boolean {
  return nowMs >= tokenSet.expiresAt - REFRESH_MARGIN_MS;
}

/**
 * Builds a token set from a token endpoint response. A refresh response may omit the refresh and
 * id token; the values of the previous set apply then. Without expires_in the access token counts
 * as expired, so the next use refreshes it.
 */
export function toTokenSet(response: TokenEndpointResponse, nowMs: number, previous?: TokenSet | null): TokenSet {
  if (!response.accessToken) {
    throw new Error("The token endpoint response has no access token.");
  }
  return {
    accessToken: response.accessToken,
    refreshToken: response.refreshToken ?? previous?.refreshToken ?? null,
    idToken: response.idToken ?? previous?.idToken ?? null,
    expiresAt: response.expiresIn !== undefined ? nowMs + response.expiresIn * 1000 : nowMs,
  };
}

/** Reads a serialized token set; anything missing, malformed or incomplete yields null. */
export function parseStoredTokenSet(raw: string | null): TokenSet | null {
  if (!raw) return null;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (typeof value !== "object" || value === null) return null;
  const candidate = value as Record<string, unknown>;
  if (typeof candidate.accessToken !== "string" || typeof candidate.expiresAt !== "number") return null;
  return {
    accessToken: candidate.accessToken,
    refreshToken: typeof candidate.refreshToken === "string" ? candidate.refreshToken : null,
    idToken: typeof candidate.idToken === "string" ? candidate.idToken : null,
    expiresAt: candidate.expiresAt,
  };
}

/**
 * Decides what a finished refresh does to the session. The outcome (a refreshed set, or null for a
 * failed refresh, which logs out) applies only while the session holds the token set the refresh
 * started from. A login or logout during the refresh takes precedence, and its token set is the result.
 */
export function resolveRefreshOutcome(
  current: TokenSet | null,
  startedFrom: TokenSet,
  outcome: TokenSet | null,
): { apply: boolean; tokens: TokenSet | null } {
  if (current !== startedFrom) return { apply: false, tokens: current };
  return { apply: true, tokens: outcome };
}
