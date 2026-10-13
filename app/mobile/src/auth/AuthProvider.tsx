import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AppState } from "react-native";
import * as Crypto from "expo-crypto";
import * as WebBrowser from "expo-web-browser";
import {
  exchangeCodeAsync,
  fetchDiscoveryAsync,
  makeRedirectUri,
  Prompt,
  ResponseType,
  useAuthRequest,
  useAutoDiscovery,
  type DiscoveryDocument,
} from "expo-auth-session";
import { setAccessTokenProvider } from "../api/client";
import { authConfig } from "./config";
import { ensureDiscovery } from "./discovery";
import { toTokenSet } from "./tokenLogic";
import { requestTokenRefresh } from "./tokenRefresh";
import { createTokenSession } from "./tokenSession";
import { tokenStorage } from "./tokenStorage";

// On the web the Keycloak redirect lands in the login popup, which loads the app; this hands the
// redirect URL back to the opening window and closes the popup.
WebBrowser.maybeCompleteAuthSession();

export type AuthStatus = "loading" | "anonymous" | "authenticated";

export interface AuthContextValue {
  status: AuthStatus;
  /**
   * Opens the Keycloak login; resolves false when the user cancels it or when the login only
   * became ready (the discovery document was loaded by this call, a further call opens the login),
   * and rejects when it fails. Rejects with DiscoveryUnavailableError when Keycloak cannot be reached.
   */
  login(): Promise<boolean>;
  logout(): Promise<void>;
  /**
   * The current access token, refreshed first when it is about to expire; undefined when
   * anonymous. Rejects with SessionUnavailableError when the refresh got no usable answer; the
   * member stays signed in.
   */
  getAccessToken(): Promise<string | undefined>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function loadDiscovery(): Promise<DiscoveryDocument> {
  return fetchDiscoveryAsync(authConfig.issuer);
}

// Returning to the foreground renews the session at most this often.
const FOREGROUND_RENEW_INTERVAL_MS = 5 * 60 * 1000;

/**
 * Holds the Keycloak offline session of the app user and supplies its access token to the API
 * client. The session lasts until the member signs out or Keycloak refuses to renew it; opening
 * the app renews it once, so a suspended or deleted account is signed out on the next start.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const autoDiscovery = useAutoDiscovery(authConfig.issuer);
  // The document a login tap loaded after the automatic discovery at start failed.
  const [tappedDiscovery, setTappedDiscovery] = useState<DiscoveryDocument | null>(null);
  const discovery = autoDiscovery ?? tappedDiscovery;
  const redirectUri = useMemo(
    () => makeRedirectUri({ scheme: authConfig.redirectScheme, path: authConfig.redirectPath }),
    [],
  );
  // Every login attempt uses its own state and PKCE verifier. useAuthRequest builds a new request
  // (with a new verifier) whenever the state changes, and the request is ready before the press,
  // since the web popup has to open synchronously from it.
  const [requestState, setRequestState] = useState(() => Crypto.randomUUID());
  const [request, , promptAsync] = useAuthRequest(
    {
      clientId: authConfig.clientId,
      scopes: [...authConfig.scopes],
      redirectUri,
      responseType: ResponseType.Code,
      usePKCE: true,
      state: requestState,
      // Signing out ends the offline session; the Keycloak sign-in in the system browser may
      // outlive it, so every login asks for credentials.
      prompt: Prompt.Login,
    },
    discovery,
  );

  const [status, setStatus] = useState<AuthStatus>("loading");
  const discoveryRef = useRef(discovery);
  discoveryRef.current = discovery;

  const session = useMemo(
    () =>
      createTokenSession({
        storage: tokenStorage,
        refresh: (tokens) =>
          requestTokenRefresh(tokens, {
            tokenEndpoint: async () => {
              const { tokenEndpoint } = await ensureDiscovery(discoveryRef.current, loadDiscovery);
              if (!tokenEndpoint) throw new Error("The discovery document names no token endpoint.");
              return tokenEndpoint;
            },
            clientId: authConfig.clientId,
            fetch: (input, init) => fetch(input, init),
            now: Date.now,
          }),
        now: Date.now,
        onChange: (tokens) => setStatus(tokens ? "authenticated" : "anonymous"),
      }),
    [],
  );

  useEffect(() => {
    let lastRenewal = Date.now();
    void session.restore().then((tokens) => {
      if (tokens) void session.renew();
    });
    const subscription = AppState.addEventListener("change", (next) => {
      if (next !== "active" || Date.now() - lastRenewal < FOREGROUND_RENEW_INTERVAL_MS) return;
      lastRenewal = Date.now();
      void session.renew();
    });
    return () => subscription.remove();
  }, [session]);

  const login = useCallback(async () => {
    if (!discovery) {
      try {
        setTappedDiscovery(await ensureDiscovery(null, loadDiscovery));
      } catch (error) {
        console.error("Loading the Keycloak discovery document failed.", error);
        throw error;
      }
      return false;
    }
    if (!request || request.state !== requestState) return false;
    let result;
    try {
      result = await promptAsync();
    } finally {
      setRequestState(Crypto.randomUUID());
    }
    if (result.type === "error") {
      throw result.error ?? new Error(result.params.error_description ?? result.params.error ?? "Login failed.");
    }
    if (result.type !== "success") return false;
    const response = await exchangeCodeAsync(
      {
        clientId: authConfig.clientId,
        code: result.params.code,
        redirectUri,
        extraParams: request.codeVerifier ? { code_verifier: request.codeVerifier } : undefined,
      },
      discovery,
    );
    await session.signIn(toTokenSet(response, Date.now()));
    return true;
  }, [request, requestState, discovery, promptAsync, redirectUri, session]);

  const logout = useCallback(async () => {
    const tokens = await session.signOut();
    if (!tokens?.idToken && !tokens?.refreshToken) return;
    // Ends the Keycloak sessions as well, so the next login asks for credentials and the offline
    // token is revoked. Keycloak finds them from the id token hint and the refresh token. Best
    // effort: the local session is gone either way.
    const params = new URLSearchParams({ client_id: authConfig.clientId });
    if (tokens.idToken) params.set("id_token_hint", tokens.idToken);
    if (tokens.refreshToken) params.set("refresh_token", tokens.refreshToken);
    try {
      const { endSessionEndpoint } = await ensureDiscovery(discoveryRef.current, loadDiscovery);
      if (!endSessionEndpoint) return;
      await fetch(endSessionEndpoint, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: params.toString(),
      });
    } catch {
      // Keycloak unreachable: its sessions stay until their idle timeout, and the device keeps no token for them.
    }
  }, [session]);

  const getAccessToken = useCallback(() => session.getAccessToken(), [session]);

  useEffect(() => {
    setAccessTokenProvider(getAccessToken);
    return () => setAccessTokenProvider(null);
  }, [getAccessToken]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, login, logout, getAccessToken }),
    [status, login, logout, getAccessToken],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider.");
  return value;
}
