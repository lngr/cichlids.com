import { useCallback, useState } from "react";
import { useAuth } from "./AuthProvider";
import { DiscoveryUnavailableError } from "./discovery";

export type LoginFailure = "unavailable" | "failed";

/**
 * The state of a login button: busy while a login runs and the kind of the last failure, if any.
 * "unavailable" means the Keycloak discovery document could not be loaded; pressing again retries.
 */
export function useLoginAction(): { busy: boolean; failure: LoginFailure | null; onLogin: () => void } {
  const { login } = useAuth();
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<LoginFailure | null>(null);

  // login() runs directly in the press handler: on the web it opens the Keycloak popup, which
  // browsers only allow as the immediate result of a user gesture.
  const onLogin = useCallback(() => {
    setFailure(null);
    setBusy(true);
    login()
      .catch((error: unknown) => setFailure(error instanceof DiscoveryUnavailableError ? "unavailable" : "failed"))
      .finally(() => setBusy(false));
  }, [login]);

  return { busy, failure, onLogin };
}
