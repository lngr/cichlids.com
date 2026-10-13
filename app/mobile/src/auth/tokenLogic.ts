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

/** The Keycloak account id (the sub claim) of an access token, or null when it has none or is no JWT. */
export function accountIdOf(accessToken: string): string | null {
  const payload = accessToken.split(".")[1];
  if (!payload) return null;
  try {
    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(payload.length / 4) * 4, "=");
    const claims = JSON.parse(atob(base64)) as { sub?: unknown };
    return typeof claims.sub === "string" && claims.sub !== "" ? claims.sub : null;
  } catch {
    return null;
  }
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
 * The result of one refresh attempt. "refused" means the identity provider rejected the refresh
 * token (the account was suspended or deleted, or the session was ended), which ends the session.
 * "unavailable" means no usable answer arrived (no network, a timeout or a server error); the
 * tokens stay valid for the next attempt.
 */
export type RefreshOutcome = { kind: "refreshed"; tokens: TokenSet } | { kind: "refused" } | { kind: "unavailable" };

/**
 * Classifies the token endpoint's answer to a refresh request. HTTP 400 and 401 are a refusal,
 * whatever the body says; a 2xx answer with an access token is the refreshed set; every other
 * answer counts as unavailable.
 */
export function classifyRefreshResponse(status: number, body: unknown, nowMs: number, previous: TokenSet): RefreshOutcome {
  if (status === 400 || status === 401) return { kind: "refused" };
  if (status < 200 || status >= 300 || typeof body !== "object" || body === null) return { kind: "unavailable" };
  const fields = body as Record<string, unknown>;
  if (typeof fields.access_token !== "string" || fields.access_token === "") return { kind: "unavailable" };
  return {
    kind: "refreshed",
    tokens: toTokenSet(
      {
        accessToken: fields.access_token,
        refreshToken: typeof fields.refresh_token === "string" ? fields.refresh_token : undefined,
        idToken: typeof fields.id_token === "string" ? fields.id_token : undefined,
        expiresIn: typeof fields.expires_in === "number" ? fields.expires_in : undefined,
      },
      nowMs,
      previous,
    ),
  };
}

/**
 * Decides what a finished refresh does to the session. The outcome applies only while the session
 * holds the token set the refresh started from: a refreshed set replaces it, a refusal signs out,
 * and an unavailable identity provider leaves the session as it is. A login or logout during the
 * refresh takes precedence, and its token set is the result.
 */
export function resolveRefreshOutcome(
  current: TokenSet | null,
  startedFrom: TokenSet,
  outcome: RefreshOutcome,
): { apply: boolean; tokens: TokenSet | null } {
  if (current !== startedFrom || outcome.kind === "unavailable") return { apply: false, tokens: current };
  return { apply: true, tokens: outcome.kind === "refreshed" ? outcome.tokens : null };
}
