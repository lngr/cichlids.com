// Story: TASK-3.41
import { describe, expect, it, vi } from "vitest";
import { DiscoveryUnavailableError } from "./discovery";
import { endWebSession, readWebSession, startWebLogin } from "./webSession";

const apiUrl = "https://cichlids.test";

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("readWebSession", () => {
  it("reads the session state with the cookie and keeps the CSRF token", async () => {
    const fetchImpl = vi.fn(async () => json(200, { authenticated: true, csrfToken: "csrf" }));

    expect(await readWebSession(apiUrl, fetchImpl as unknown as typeof fetch)).toEqual({ authenticated: true, csrfToken: "csrf" });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${apiUrl}/api/auth/session`);
    expect(init.credentials).toBe("include");
  });

  it("reports no session", async () => {
    const fetchImpl = vi.fn(async () => json(200, { authenticated: false, csrfToken: null }));
    expect(await readWebSession(apiUrl, fetchImpl as unknown as typeof fetch)).toEqual({ authenticated: false, csrfToken: null });
  });

  it("rejects when the API gives no usable answer", async () => {
    const offline = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    });
    await expect(readWebSession(apiUrl, offline as unknown as typeof fetch)).rejects.toThrow();
    const failing = vi.fn(async () => new Response("Bad Gateway", { status: 502 }));
    await expect(readWebSession(apiUrl, failing as unknown as typeof fetch)).rejects.toThrow();
  });
});

describe("startWebLogin", () => {
  it("posts the return address and answers the Keycloak address", async () => {
    const fetchImpl = vi.fn(async () => json(200, { authorizationUrl: "https://auth.cichlids.test/auth?state=s" }));

    expect(await startWebLogin(apiUrl, "https://cichlids.test/me", fetchImpl as unknown as typeof fetch)).toBe(
      "https://auth.cichlids.test/auth?state=s",
    );
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${apiUrl}/api/auth/login`);
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(JSON.parse(String(init.body))).toEqual({ returnUrl: "https://cichlids.test/me" });
  });

  it("reports an unreachable Keycloak or API as unavailable", async () => {
    const keycloakDown = vi.fn(async () => json(503, { code: "identity_provider_unavailable" }));
    await expect(startWebLogin(apiUrl, "/", keycloakDown as unknown as typeof fetch)).rejects.toBeInstanceOf(DiscoveryUnavailableError);
    const offline = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    });
    await expect(startWebLogin(apiUrl, "/", offline as unknown as typeof fetch)).rejects.toBeInstanceOf(DiscoveryUnavailableError);
  });

  it("rejects other failures as a failed login", async () => {
    const refused = vi.fn(async () => json(400, { code: "invalid_return_url" }));
    const error = await startWebLogin(apiUrl, "/", refused as unknown as typeof fetch).catch((err) => err);
    expect(error).toBeInstanceOf(Error);
    expect(error).not.toBeInstanceOf(DiscoveryUnavailableError);
  });
});

describe("endWebSession", () => {
  it("posts the logout with the CSRF token and the cookie", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 204 }));

    await endWebSession(apiUrl, "csrf", fetchImpl as unknown as typeof fetch);

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${apiUrl}/api/auth/logout`);
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(new Headers(init.headers).get("X-CSRF-Token")).toBe("csrf");
  });
});
