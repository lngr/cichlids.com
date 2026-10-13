import { describe, expect, it, vi } from "vitest";
import type { TokenSet } from "./tokenLogic";
import { requestTokenRefresh } from "./tokenRefresh";

const now = Date.UTC(2026, 9, 13, 8, 0, 0);
const endpoint = "https://auth.example.org/realms/cichlids/protocol/openid-connect/token";
const tokens: TokenSet = { accessToken: "old-access", refreshToken: "offline-refresh", idToken: "id", expiresAt: now - 1 };

function options(fetchImpl: typeof fetch, overrides: Partial<Parameters<typeof requestTokenRefresh>[1]> = {}) {
  return { tokenEndpoint: async () => endpoint, clientId: "cichlids-app", fetch: fetchImpl, now: () => now, timeoutMs: 50, ...overrides };
}

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("requestTokenRefresh", () => {
  it("posts the refresh token grant of the public client to the token endpoint", async () => {
    const fetchImpl = vi.fn(async () => jsonResponse(200, { access_token: "new-access", expires_in: 300 }));

    const outcome = await requestTokenRefresh(tokens, options(fetchImpl as unknown as typeof fetch));

    expect(outcome).toEqual({
      kind: "refreshed",
      tokens: { accessToken: "new-access", refreshToken: "offline-refresh", idToken: "id", expiresAt: now + 300_000 },
    });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(endpoint);
    expect(init.method).toBe("POST");
    expect(Object.fromEntries(new URLSearchParams(String(init.body)))).toEqual({
      grant_type: "refresh_token",
      client_id: "cichlids-app",
      refresh_token: "offline-refresh",
    });
  });

  it("reports a refusal of the identity provider", async () => {
    const fetchImpl = vi.fn(async () => jsonResponse(400, { error: "invalid_grant", error_description: "User disabled" }));
    expect(await requestTokenRefresh(tokens, options(fetchImpl as unknown as typeof fetch))).toEqual({ kind: "refused" });
  });

  it("reports a token set without a refresh token as refused without a request", async () => {
    const fetchImpl = vi.fn();
    expect(await requestTokenRefresh({ ...tokens, refreshToken: null }, options(fetchImpl as unknown as typeof fetch))).toEqual({
      kind: "refused",
    });
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("reports a network failure as unavailable", async () => {
    const fetchImpl = vi.fn(async () => {
      throw new TypeError("Network request failed");
    });
    expect(await requestTokenRefresh(tokens, options(fetchImpl as unknown as typeof fetch))).toEqual({ kind: "unavailable" });
  });

  it("reports a server error with an HTML body as unavailable", async () => {
    const fetchImpl = vi.fn(async () => new Response("<html>Bad Gateway</html>", { status: 502 }));
    expect(await requestTokenRefresh(tokens, options(fetchImpl as unknown as typeof fetch))).toEqual({ kind: "unavailable" });
  });

  it("reports an unreachable discovery document as unavailable", async () => {
    const fetchImpl = vi.fn();
    const outcome = await requestTokenRefresh(
      tokens,
      options(fetchImpl as unknown as typeof fetch, {
        tokenEndpoint: async () => {
          throw new Error("discovery failed");
        },
      }),
    );
    expect(outcome).toEqual({ kind: "unavailable" });
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("gives up on a request that does not answer in time and reports it as unavailable", async () => {
    const fetchImpl = vi.fn(
      (_url: string, init: RequestInit) =>
        new Promise<Response>((_resolve, reject) => {
          init.signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
        }),
    );
    expect(await requestTokenRefresh(tokens, options(fetchImpl as unknown as typeof fetch, { timeoutMs: 10 }))).toEqual({
      kind: "unavailable",
    });
  });
});
