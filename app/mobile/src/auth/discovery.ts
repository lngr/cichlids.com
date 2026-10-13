import type { DiscoveryDocument } from "expo-auth-session";

/** The Keycloak discovery document could not be loaded, for example because the issuer is unreachable. */
export class DiscoveryUnavailableError extends Error {
  constructor(cause: unknown) {
    super("The Keycloak discovery document could not be loaded.", { cause });
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
