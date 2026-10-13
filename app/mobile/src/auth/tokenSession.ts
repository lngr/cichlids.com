import { needsRefresh, resolveRefreshOutcome, type RefreshOutcome, type TokenSet } from "./tokenLogic";

/** The session could not be renewed because the identity provider gave no usable answer; the tokens stay. */
export class SessionUnavailableError extends Error {
  constructor() {
    super("The session could not be renewed because the identity provider cannot be reached.");
    this.name = "SessionUnavailableError";
  }
}

export interface TokenSessionStorage {
  get(): Promise<TokenSet | null>;
  set(tokens: TokenSet): Promise<void>;
  clear(): Promise<void>;
}

export interface TokenSessionOptions {
  storage: TokenSessionStorage;
  refresh(tokens: TokenSet): Promise<RefreshOutcome>;
  now(): number;
  /** Receives the token set after every change, null when signed out. */
  onChange(tokens: TokenSet | null): void;
}

export interface TokenSession {
  /** Loads the stored token set; an unreadable store counts as signed out. */
  restore(): Promise<TokenSet | null>;
  /**
   * Renews the session once with the identity provider, also while the access token is valid. A
   * refusal signs out; an unreachable identity provider leaves the session as it is.
   */
  renew(): Promise<void>;
  /**
   * The access token, refreshed first when it is about to expire. Undefined when signed out or
   * when the refresh was refused, which signs out. Rejects with SessionUnavailableError when the
   * identity provider cannot be reached; the session stays.
   */
  getAccessToken(): Promise<string | undefined>;
  signIn(tokens: TokenSet): Promise<void>;
  /** Signs out and returns the token set the session held, if any. */
  signOut(): Promise<TokenSet | null>;
}

/**
 * The token set of one device session: kept in its storage until the member signs out or the
 * identity provider refuses a refresh. Concurrent callers share one refresh of the same token set,
 * since the identity provider may rotate the refresh token.
 */
export function createTokenSession(options: TokenSessionOptions): TokenSession {
  let current: TokenSet | null = null;
  let pending: { from: TokenSet; promise: Promise<RefreshOutcome> } | null = null;

  async function apply(tokens: TokenSet | null): Promise<void> {
    current = tokens;
    options.onChange(tokens);
    if (tokens) await options.storage.set(tokens);
    else await options.storage.clear();
  }

  function refreshOnce(from: TokenSet): Promise<RefreshOutcome> {
    if (pending?.from !== from) {
      const entry = { from, promise: options.refresh(from) };
      pending = entry;
      void entry.promise.finally(() => {
        if (pending === entry) pending = null;
      });
    }
    return pending.promise;
  }

  async function refresh(from: TokenSet): Promise<{ tokens: TokenSet | null; unavailable: boolean }> {
    const outcome = await refreshOnce(from);
    const resolved = resolveRefreshOutcome(current, from, outcome);
    if (resolved.apply) await apply(resolved.tokens);
    return { tokens: resolved.tokens, unavailable: outcome.kind === "unavailable" && current === from };
  }

  return {
    async restore() {
      const stored = await options.storage.get().catch(() => null);
      current = stored;
      options.onChange(stored);
      return stored;
    },
    async renew() {
      if (current) await refresh(current);
    },
    async getAccessToken() {
      const tokens = current;
      if (!tokens) return undefined;
      if (!needsRefresh(tokens, options.now())) return tokens.accessToken;
      const result = await refresh(tokens);
      if (result.unavailable) throw new SessionUnavailableError();
      return result.tokens?.accessToken;
    },
    async signIn(tokens) {
      await apply(tokens);
    },
    async signOut() {
      const previous = current;
      await apply(null);
      return previous;
    },
  };
}
