import { DiscoveryUnavailableError } from "./discovery";

/** The web session as the API reports it; csrfToken is null without a session. */
export interface WebSessionInfo {
  authenticated: boolean;
  csrfToken: string | null;
}

/**
 * Reads the session of this browser from the API, which renews it with Keycloak on the way.
 * Rejects when the API gives no usable answer.
 */
export async function readWebSession(apiUrl: string, fetchImpl: typeof fetch): Promise<WebSessionInfo> {
  const response = await fetchImpl(`${apiUrl}/api/auth/session`, {
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`The session endpoint answered ${response.status}.`);
  const body = (await response.json()) as { authenticated?: unknown; csrfToken?: unknown };
  const authenticated = body.authenticated === true;
  return { authenticated, csrfToken: authenticated && typeof body.csrfToken === "string" ? body.csrfToken : null };
}

/**
 * Starts a login at the API and resolves with the Keycloak address to navigate to. Rejects with
 * DiscoveryUnavailableError when the API or Keycloak cannot be reached.
 */
export async function startWebLogin(apiUrl: string, returnUrl: string, fetchImpl: typeof fetch): Promise<string> {
  let response: Response;
  try {
    response = await fetchImpl(`${apiUrl}/api/auth/login`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ returnUrl }),
    });
  } catch (error) {
    throw new DiscoveryUnavailableError(error);
  }
  if (response.status === 503) throw new DiscoveryUnavailableError(new Error("The API cannot reach Keycloak."));
  if (!response.ok) throw new Error(`The login start answered ${response.status}.`);
  const body = (await response.json()) as { authorizationUrl?: unknown };
  if (typeof body.authorizationUrl !== "string") throw new Error("The login start named no Keycloak address.");
  return body.authorizationUrl;
}

/** Ends the session at the API, which revokes it at Keycloak and clears the cookie. */
export async function endWebSession(apiUrl: string, csrfToken: string | null, fetchImpl: typeof fetch): Promise<void> {
  const headers: Record<string, string> = {};
  if (csrfToken) headers["X-CSRF-Token"] = csrfToken;
  await fetchImpl(`${apiUrl}/api/auth/logout`, { method: "POST", credentials: "include", headers });
}
