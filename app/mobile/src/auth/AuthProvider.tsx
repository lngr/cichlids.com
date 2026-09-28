import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import * as Crypto from "expo-crypto";
import * as WebBrowser from "expo-web-browser";
import {
  exchangeCodeAsync,
  fetchDiscoveryAsync,
  makeRedirectUri,
  refreshAsync,
  ResponseType,
  useAuthRequest,
  useAutoDiscovery,
  type DiscoveryDocument,
} from "expo-auth-session";
import { setAccessTokenProvider } from "../api/client";
import { authConfig } from "./config";
import { needsRefresh, resolveRefreshOutcome, toTokenSet, type TokenSet } from "./tokenLogic";
import { tokenStorage } from "./tokenStorage";

// On the web the Keycloak redirect lands in the login popup, which loads the app; this hands the
// redirect URL back to the opening window and closes the popup.
WebBrowser.maybeCompleteAuthSession();

export type AuthStatus = "loading" | "anonymous" | "authenticated";

export interface AuthContextValue {
  status: AuthStatus;
  accessToken: string | null;
  /** True once the Keycloak discovery document and the PKCE request are ready for login(). */
  loginReady: boolean;
  /** Opens the Keycloak login; resolves false when the user cancels it and rejects when it fails. */
  login(): Promise<boolean>;
  logout(): Promise<void>;
  /** The current access token, refreshed first when it is about to expire; undefined when anonymous. */
  getAccessToken(): Promise<string | undefined>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

async function resolveDiscovery(loaded: DiscoveryDocument | null): Promise<DiscoveryDocument> {
  return loaded ?? (await fetchDiscoveryAsync(authConfig.issuer));
}

/** Holds the Keycloak session of the app user and supplies its access token to the API client. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const discovery = useAutoDiscovery(authConfig.issuer);
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
    },
    discovery,
  );

  const [state, setState] = useState<{ status: AuthStatus; accessToken: string | null }>({
    status: "loading",
    accessToken: null,
  });
  const tokensRef = useRef<TokenSet | null>(null);
  const refreshRef = useRef<{ from: TokenSet; promise: Promise<TokenSet | null> } | null>(null);
  const discoveryRef = useRef(discovery);
  discoveryRef.current = discovery;

  const applyTokens = useCallback(async (tokens: TokenSet | null) => {
    tokensRef.current = tokens;
    setState(tokens ? { status: "authenticated", accessToken: tokens.accessToken } : { status: "anonymous", accessToken: null });
    if (tokens) {
      await tokenStorage.set(tokens);
    } else {
      await tokenStorage.clear();
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    tokenStorage
      .get()
      .catch(() => null)
      .then((tokens) => {
        if (cancelled) return;
        tokensRef.current = tokens;
        setState(tokens ? { status: "authenticated", accessToken: tokens.accessToken } : { status: "anonymous", accessToken: null });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const login = useCallback(async () => {
    if (!request || !discovery || request.state !== requestState) return false;
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
    await applyTokens(toTokenSet(response, Date.now()));
    return true;
  }, [request, requestState, discovery, promptAsync, redirectUri, applyTokens]);

  const logout = useCallback(async () => {
    const tokens = tokensRef.current;
    await applyTokens(null);
    if (!tokens?.idToken && !tokens?.refreshToken) return;
    // Ends the Keycloak session as well, so the next login asks for credentials. Keycloak finds the
    // session from the id token hint or the refresh token. Best effort: the local session is gone
    // either way.
    const params = new URLSearchParams({ client_id: authConfig.clientId });
    if (tokens.idToken) params.set("id_token_hint", tokens.idToken);
    if (tokens.refreshToken) params.set("refresh_token", tokens.refreshToken);
    try {
      const { endSessionEndpoint } = await resolveDiscovery(discoveryRef.current);
      if (!endSessionEndpoint) return;
      await fetch(endSessionEndpoint, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: params.toString(),
      });
    } catch {
      // Keycloak unreachable: its session expires on its own.
    }
  }, [applyTokens]);

  const refresh = useCallback(
    async (tokens: TokenSet): Promise<TokenSet | null> => {
      let outcome: TokenSet | null;
      try {
        if (!tokens.refreshToken) throw new Error("No refresh token.");
        const response = await refreshAsync(
          { clientId: authConfig.clientId, refreshToken: tokens.refreshToken, scopes: [...authConfig.scopes] },
          await resolveDiscovery(discoveryRef.current),
        );
        outcome = toTokenSet(response, Date.now(), tokens);
      } catch {
        outcome = null;
      }
      const resolved = resolveRefreshOutcome(tokensRef.current, tokens, outcome);
      if (resolved.apply) await applyTokens(resolved.tokens);
      return resolved.tokens;
    },
    [applyTokens],
  );

  const getAccessToken = useCallback(async () => {
    const tokens = tokensRef.current;
    if (!tokens) return undefined;
    if (!needsRefresh(tokens, Date.now())) return tokens.accessToken;
    // Concurrent API calls share one refresh of the same token set, since Keycloak may rotate the
    // refresh token.
    if (refreshRef.current?.from !== tokens) {
      const pending = { from: tokens, promise: refresh(tokens) };
      refreshRef.current = pending;
      void pending.promise.finally(() => {
        if (refreshRef.current === pending) refreshRef.current = null;
      });
    }
    return (await refreshRef.current.promise)?.accessToken;
  }, [refresh]);

  useEffect(() => {
    setAccessTokenProvider(getAccessToken);
    return () => setAccessTokenProvider(null);
  }, [getAccessToken]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status: state.status,
      accessToken: state.accessToken,
      loginReady: request !== null && request.state === requestState && discovery !== null,
      login,
      logout,
      getAccessToken,
    }),
    [state, request, requestState, discovery, login, logout, getAccessToken],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider.");
  return value;
}
