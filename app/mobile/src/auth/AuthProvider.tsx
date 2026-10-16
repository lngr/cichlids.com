import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AppState } from "react-native";
import * as Crypto from "expo-crypto";
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
import { AuthContext, type AuthContextValue, type AuthStatus } from "./authContext";
import { authConfig } from "./config";
import { ensureDiscovery } from "./discovery";
import { accountIdOf, toTokenSet } from "./tokenLogic";
import { requestTokenRefresh } from "./tokenRefresh";
import { createTokenSession } from "./tokenSession";
import { tokenStorage } from "./tokenStorage";
import { useAccountStatus } from "../deviceAccount/useAccountStatus";

export { useAuth, type AuthContextValue, type AuthStatus } from "./authContext";

function loadDiscovery(): Promise<DiscoveryDocument> {
  return fetchDiscoveryAsync(authConfig.issuer);
}

// Returning to the foreground renews the session at most this often.
const FOREGROUND_RENEW_INTERVAL_MS = 5 * 60 * 1000;

/**
 * Holds the Keycloak offline session of the native app user and supplies its access token to the
 * API client; the web build uses AuthProvider.web.tsx. The session lasts until the member signs out or Keycloak refuses to renew it; opening
 * the app renews it once, so a suspended or deleted account is signed out on the next start.
 * Without a session the app acts for the device account of the installation (see useAccountStatus).
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
  // (with a new verifier) whenever the state changes, and the request is ready before the press.
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

  const [sessionStatus, setStatus] = useState<AuthStatus>("loading");
  const [sessionAccountId, setAccountId] = useState<string | null>(null);
  const { status, accountId, provisional, registrationPending, leftBehind } = useAccountStatus(sessionStatus, sessionAccountId);
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
        onChange: (tokens) => {
          setStatus(tokens ? "authenticated" : "anonymous");
          setAccountId(tokens ? accountIdOf(tokens.accessToken) : null);
        },
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
    // Native logout clears the app's offline session, and Prompt.Login requests credentials for
    // every login. Web logout ends the API session and Keycloak browser session through its end
    // session endpoint.
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
    () => ({ status, accountId, provisional, registrationPending, deviceContentLeftBehind: leftBehind, login, signInFailed: false, logout, getAccessToken }),
    [status, accountId, provisional, registrationPending, leftBehind, login, logout, getAccessToken],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
