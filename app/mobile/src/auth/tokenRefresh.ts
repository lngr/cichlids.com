import { classifyRefreshResponse, type RefreshOutcome, type TokenSet } from "./tokenLogic";

/** A refresh that has not answered after this time counts as unavailable. */
export const REFRESH_TIMEOUT_MS = 15_000;

export interface TokenRefreshOptions {
  /** Resolves the token endpoint of the issuer; a rejection counts as unavailable. */
  tokenEndpoint: () => Promise<string>;
  clientId: string;
  fetch: typeof fetch;
  now: () => number;
  timeoutMs?: number;
}

/**
 * Requests a new token set with the refresh token of the given set and classifies the answer. It
 * never rejects: a set without a refresh token is refused, and a missing endpoint, a network
 * failure or a timeout are unavailable.
 */
export async function requestTokenRefresh(tokens: TokenSet, options: TokenRefreshOptions): Promise<RefreshOutcome> {
  if (!tokens.refreshToken) return { kind: "refused" };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? REFRESH_TIMEOUT_MS);
  try {
    const endpoint = await options.tokenEndpoint();
    const response = await options.fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded", Accept: "application/json" },
      body: new URLSearchParams({
        grant_type: "refresh_token",
        client_id: options.clientId,
        refresh_token: tokens.refreshToken,
      }).toString(),
      signal: controller.signal,
    });
    const text = await response.text();
    return classifyRefreshResponse(response.status, parseJson(text), options.now(), tokens);
  } catch {
    return { kind: "unavailable" };
  } finally {
    clearTimeout(timer);
  }
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}
