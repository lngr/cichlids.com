import { useCallback, useState } from "react";
import { useAuth } from "./AuthProvider";
import { DiscoveryUnavailableError } from "./discovery";

export type LoginFailure = "unavailable" | "failed";

/**
 * The state of a login button: busy while a login runs and the kind of the last failure, if any.
 * "unavailable" means Keycloak could not be reached for the login; pressing again retries. A
 * login that returned to the web app without a session shows as "failed" from the start.
 */
export function useLoginAction(): { busy: boolean; failure: LoginFailure | null; onLogin: () => void } {
  const { login, signInFailed } = useAuth();
  const [busy, setBusy] = useState(false);
  const [attemptFailure, setAttemptFailure] = useState<LoginFailure | null>(null);
  const failure = attemptFailure ?? (signInFailed ? "failed" : null);

  const onLogin = useCallback(() => {
    setAttemptFailure(null);
    setBusy(true);
    login()
      .catch((error: unknown) => setAttemptFailure(error instanceof DiscoveryUnavailableError ? "unavailable" : "failed"))
      .finally(() => setBusy(false));
  }, [login]);

  return { busy, failure, onLogin };
}
