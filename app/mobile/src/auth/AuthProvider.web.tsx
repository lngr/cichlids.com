import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { API_BASE_URL, setWebSessionCsrfToken, setWebSessionEndedListener } from "../api/client";
import { AuthContext, type AuthContextValue, type AuthStatus } from "./authContext";
import { endWebSession, readWebSession, startWebLogin, takeSignInFailure } from "./webSession";

export { useAuth, type AuthContextValue, type AuthStatus } from "./authContext";

// While the API cannot be reached, the session state is asked for again this often.
const SESSION_RETRY_MS = 5_000;

// Removes the failure flag from the address, so a reload does not repeat the message. Runs after
// the first answer of the API, when the router has settled on the address.
function forgetSignInFailureInAddress() {
  const { failed, cleanUrl } = takeSignInFailure(window.location.href);
  if (failed) window.history.replaceState(window.history.state, "", cleanUrl);
}

const browserFetch: typeof fetch = (input, init) => fetch(input, init);

/**
 * Holds the web app's session, which the API keeps in an httpOnly cookie (backend for frontend):
 * no token ever reaches the page. Opening the app asks the API for the session state and its CSRF
 * token; while the API cannot be reached the state stays loading and is asked for again. A 401
 * answer of the API ends the session in the app as well. Signing out ends the session at the API
 * and then passes the page through Keycloak's end session address. A login that returned without a session
 * carries a flag in the address; it is removed from the address and reported as signInFailed.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [accountId, setAccountId] = useState<string | null>(null);
  const [signInFailed, setSignInFailed] = useState(() => takeSignInFailure(window.location.href).failed);

  useEffect(() => {
    let cancelled = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const load = () => {
      readWebSession(API_BASE_URL, browserFetch)
        .then((session) => {
          if (cancelled) return;
          forgetSignInFailureInAddress();
          setWebSessionCsrfToken(session.csrfToken);
          setCsrfToken(session.csrfToken);
          setAccountId(session.accountId);
          setStatus(session.authenticated ? "authenticated" : "anonymous");
        })
        .catch(() => {
          if (!cancelled) retry = setTimeout(load, SESSION_RETRY_MS);
        });
    };
    load();
    setWebSessionEndedListener(() => {
      setWebSessionCsrfToken(null);
      setCsrfToken(null);
      setAccountId(null);
      setStatus("anonymous");
    });
    return () => {
      cancelled = true;
      clearTimeout(retry);
      setWebSessionEndedListener(null);
    };
  }, []);

  // The page leaves for Keycloak and comes back through the API's callback, so a started login
  // never resolves here.
  const login = useCallback(async () => {
    setSignInFailed(false);
    const authorizationUrl = await startWebLogin(API_BASE_URL, window.location.href, browserFetch);
    window.location.assign(authorizationUrl);
    return new Promise<boolean>(() => {});
  }, []);

  // The page leaves for Keycloak, which ends the Keycloak sign-in of this browser and returns to
  // the app signed out.
  const logout = useCallback(async () => {
    setWebSessionCsrfToken(null);
    setCsrfToken(null);
    setAccountId(null);
    setStatus("anonymous");
    let logoutUrl: string | null = null;
    try {
      logoutUrl = await endWebSession(API_BASE_URL, csrfToken, browserFetch);
    } catch {
      // The API is unreachable: the server session stays until the next sign-out.
    }
    if (logoutUrl) window.location.assign(logoutUrl);
  }, [csrfToken]);

  const getAccessToken = useCallback(async () => undefined, []);

  const value = useMemo<AuthContextValue>(
    () => ({ status, accountId, login, signInFailed, logout, getAccessToken }),
    [status, accountId, login, signInFailed, logout, getAccessToken],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
