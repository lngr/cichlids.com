import type { DiscoveryDocument } from "expo-auth-session";

/**
 * Keycloak cannot be reached for a login: the discovery document could not be loaded, or on the
 * web the API could not start the login.
 */
export class DiscoveryUnavailableError extends Error {
  constructor(cause: unknown) {
    super("Keycloak cannot be reached for a login.", { cause });
    this.name = "DiscoveryUnavailableError";
  }
}

/**
 * The loaded discovery document, or a freshly loaded one when none exists yet. A failed load
 * rejects with DiscoveryUnavailableError and leaves nothing cached, so the next call loads again.
 */
export async function ensureDiscovery(
  loaded: DiscoveryDocument | null,
  load: () => Promise<DiscoveryDocument>,
): Promise<DiscoveryDocument> {
  if (loaded) return loaded;
  try {
    return await load();
  } catch (error) {
    throw new DiscoveryUnavailableError(error);
  }
}
