import { describe, expect, it } from "vitest";
import {
  classifyRefreshResponse,
  needsRefresh,
  parseStoredTokenSet,
  REFRESH_MARGIN_MS,
  resolveRefreshOutcome,
  toTokenSet,
  type TokenSet,
} from "./tokenLogic";

const now = Date.UTC(2026, 8, 28, 12, 0, 0);

function tokenSet(overrides: Partial<TokenSet> = {}): TokenSet {
  return { accessToken: "access", refreshToken: "refresh", idToken: "id", expiresAt: now + 300_000, ...overrides };
}

describe("needsRefresh", () => {
  it("is false while the access token is valid for longer than the margin", () => {
    expect(needsRefresh(tokenSet({ expiresAt: now + REFRESH_MARGIN_MS + 1 }), now)).toBe(false);
  });

  it("is true within 30 seconds of expiry", () => {
    expect(REFRESH_MARGIN_MS).toBe(30_000);
    expect(needsRefresh(tokenSet({ expiresAt: now + 29_999 }), now)).toBe(true);
    expect(needsRefresh(tokenSet({ expiresAt: now + REFRESH_MARGIN_MS }), now)).toBe(true);
  });

  it("is true once the access token has expired", () => {
    expect(needsRefresh(tokenSet({ expiresAt: now - 1 }), now)).toBe(true);
  });
});

describe("toTokenSet", () => {
  it("turns the relative expires_in into an absolute expiresAt", () => {
    const result = toTokenSet({ accessToken: "a", refreshToken: "r", idToken: "i", expiresIn: 300 }, now);
    expect(result).toEqual({ accessToken: "a", refreshToken: "r", idToken: "i", expiresAt: now + 300_000 });
  });

  it("keeps the previous refresh and id token when a refresh response omits them", () => {
    const result = toTokenSet({ accessToken: "a2", expiresIn: 60 }, now, tokenSet());
    expect(result).toEqual({ accessToken: "a2", refreshToken: "refresh", idToken: "id", expiresAt: now + 60_000 });
  });

  it("uses null for tokens the response and the previous set both lack", () => {
    const result = toTokenSet({ accessToken: "a", expiresIn: 60 }, now);
    expect(result.refreshToken).toBeNull();
    expect(result.idToken).toBeNull();
  });

  it("treats a response without expires_in as expiring immediately", () => {
    const result = toTokenSet({ accessToken: "a", refreshToken: "r" }, now);
    expect(result.expiresAt).toBe(now);
    expect(needsRefresh(result, now)).toBe(true);
  });

  it("rejects a response without an access token", () => {
    expect(() => toTokenSet({ accessToken: "", expiresIn: 60 }, now)).toThrow();
  });
});

describe("parseStoredTokenSet", () => {
  it("round-trips a serialized token set", () => {
    const stored = tokenSet();
    expect(parseStoredTokenSet(JSON.stringify(stored))).toEqual(stored);
  });

  it("returns null for missing, malformed or incomplete values", () => {
    expect(parseStoredTokenSet(null)).toBeNull();
    expect(parseStoredTokenSet("not json")).toBeNull();
    expect(parseStoredTokenSet(JSON.stringify({ accessToken: "a" }))).toBeNull();
    expect(parseStoredTokenSet(JSON.stringify({ accessToken: 1, expiresAt: now }))).toBeNull();
  });
});

describe("resolveRefreshOutcome", () => {
  const startedFrom = tokenSet();
  const refreshed = tokenSet({ accessToken: "refreshed", expiresAt: now + 600_000 });

  it("applies the refreshed set while the session is the one the refresh started from", () => {
    expect(resolveRefreshOutcome(startedFrom, startedFrom, { kind: "refreshed", tokens: refreshed })).toEqual({
      apply: true,
      tokens: refreshed,
    });
  });

  it("applies a refused refresh as a sign-out while the session is unchanged", () => {
    expect(resolveRefreshOutcome(startedFrom, startedFrom, { kind: "refused" })).toEqual({ apply: true, tokens: null });
  });

  it("keeps the tokens when the identity provider could not be reached", () => {
    expect(resolveRefreshOutcome(startedFrom, startedFrom, { kind: "unavailable" })).toEqual({ apply: false, tokens: startedFrom });
  });

  it("keeps a logout that happened during the refresh", () => {
    expect(resolveRefreshOutcome(null, startedFrom, { kind: "refreshed", tokens: refreshed })).toEqual({ apply: false, tokens: null });
  });

  it("keeps a new login that happened during the refresh, also when the refresh was refused", () => {
    const newLogin = tokenSet({ accessToken: "new-login" });
    expect(resolveRefreshOutcome(newLogin, startedFrom, { kind: "refreshed", tokens: refreshed })).toEqual({ apply: false, tokens: newLogin });
    expect(resolveRefreshOutcome(newLogin, startedFrom, { kind: "refused" })).toEqual({ apply: false, tokens: newLogin });
  });
});

describe("classifyRefreshResponse", () => {
  const previous = tokenSet();

  it("turns a successful answer into the refreshed token set", () => {
    const outcome = classifyRefreshResponse(
      200,
      { access_token: "a2", refresh_token: "r2", id_token: "i2", expires_in: 300 },
      now,
      previous,
    );
    expect(outcome).toEqual({ kind: "refreshed", tokens: { accessToken: "a2", refreshToken: "r2", idToken: "i2", expiresAt: now + 300_000 } });
  });

  it("keeps the previous refresh and id token when the answer omits them", () => {
    const outcome = classifyRefreshResponse(200, { access_token: "a2", expires_in: 60 }, now, previous);
    expect(outcome).toEqual({ kind: "refreshed", tokens: { accessToken: "a2", refreshToken: "refresh", idToken: "id", expiresAt: now + 60_000 } });
  });

  it.each([
    [400, { error: "invalid_grant", error_description: "User disabled" }],
    [400, { error: "invalid_grant", error_description: "Offline user session not found" }],
    [401, { error: "invalid_client" }],
    [400, "not json"],
  ])("treats HTTP %s as refused by the identity provider", (status, body) => {
    expect(classifyRefreshResponse(status, body, now, previous)).toEqual({ kind: "refused" });
  });

  it.each([
    [500, "<html>Internal Server Error</html>"],
    [502, null],
    [503, { error: "temporarily_unavailable" }],
    [429, null],
    [200, { error: "no token" }],
    [200, "not json"],
  ])("treats HTTP %s with an unusable answer as unavailable", (status, body) => {
    expect(classifyRefreshResponse(status, body, now, previous)).toEqual({ kind: "unavailable" });
  });
});
