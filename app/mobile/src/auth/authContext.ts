import { createContext, useContext } from "react";

export type AuthStatus = "loading" | "anonymous" | "authenticated";

export interface AuthContextValue {
  /**
   * "authenticated" when the app acts for a member: a signed-in account or, on a device without
   * a session, the provisional account of the installation. "loading" also covers the wait for
   * the first registration of the device account and for the handover of its content to an
   * account that just signed in. "anonymous" is a visitor.
   */
  status: AuthStatus;
  /** The Keycloak account id of the signed-in member, "device:" and the profile id for the provisional account; null while loading and when anonymous. */
  accountId: string | null;
  /** True when the app acts for the provisional account of this installation, which may not take actions that reach other people. */
  provisional: boolean;
  /** True while a signed-in account waits for the device account to hand over its content; status is loading then. */
  registrationPending: boolean;
  /** True when the member signed in and the content of the device account stayed on the device. */
  deviceContentLeftBehind: boolean;
  /**
   * Opens the Keycloak login; resolves false when the user cancels it or when the login only
   * became ready (the discovery document was loaded by this call, a further call opens the login),
   * and rejects when it fails. Rejects with DiscoveryUnavailableError when Keycloak cannot be reached.
   */
  login(): Promise<boolean>;
  /**
   * True on the web when the last login returned without a session (refused by the member or the
   * code exchange failed), until the next login starts; always false in the native app.
   */
  signInFailed: boolean;
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
