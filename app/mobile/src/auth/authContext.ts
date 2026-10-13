import { createContext, useContext } from "react";

export type AuthStatus = "loading" | "anonymous" | "authenticated";

export interface AuthContextValue {
  status: AuthStatus;
  /** The Keycloak account id of the signed-in member; null while loading and when anonymous. */
  accountId: string | null;
  /**
   * Opens the Keycloak login; resolves false when the user cancels it or when the login only
   * became ready (the discovery document was loaded by this call, a further call opens the login),
   * and rejects when it fails. Rejects with DiscoveryUnavailableError when Keycloak cannot be reached.
   */
  login(): Promise<boolean>;
  logout(): Promise<void>;
  /**
   * The current access token for the bearer header, refreshed first when it is about to expire;
   * undefined when anonymous and on the web, where the session cookie authenticates. Rejects with
   * SessionUnavailableError when the refresh got no usable answer; the member stays signed in.
   */
  getAccessToken(): Promise<string | undefined>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider.");
  return value;
}
