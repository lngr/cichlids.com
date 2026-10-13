// Story: TASK-3.41
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { RefreshOutcome, TokenSet } from "./tokenLogic";
import { createTokenSession, SessionUnavailableError, type TokenSessionStorage } from "./tokenSession";

const DAY_MS = 24 * 60 * 60 * 1000;
const signedInAt = Date.UTC(2026, 6, 1, 9, 0, 0);
let now = signedInAt;

function tokenSet(overrides: Partial<TokenSet> = {}): TokenSet {
  return { accessToken: "access", refreshToken: "offline-refresh", idToken: "id", expiresAt: signedInAt + 300_000, ...overrides };
}

function memoryStorage(initial: TokenSet | null = null) {
  let stored = initial;
  const storage: TokenSessionStorage & { value(): TokenSet | null } = {
    get: vi.fn(async () => stored),
    set: vi.fn(async (tokens: TokenSet) => {
      stored = tokens;
    }),
    clear: vi.fn(async () => {
      stored = null;
    }),
    value: () => stored,
  };
  return storage;
}

function setup(stored: TokenSet | null, refresh: (tokens: TokenSet) => Promise<RefreshOutcome>) {
  const storage = memoryStorage(stored);
  const changes: (TokenSet | null)[] = [];
  const refreshSpy = vi.fn(refresh);
  const session = createTokenSession({ storage, refresh: refreshSpy, now: () => now, onChange: (tokens) => changes.push(tokens) });
  return { session, storage, changes, refresh: refreshSpy };
}

beforeEach(() => {
  now = signedInAt;
});

describe("a member signed in on a phone", () => {
  it("opens signed in after three months without use and renews the expired access token", async () => {
    const stored = tokenSet();
    now = signedInAt + 90 * DAY_MS;
    const renewed = tokenSet({ accessToken: "renewed", expiresAt: now + 300_000 });
    const { session, storage, changes } = setup(stored, async () => ({ kind: "refreshed", tokens: renewed }));

    expect(await session.restore()).toBe(stored);
    expect(changes).toEqual([stored]);

    await session.renew();

    expect(changes.at(-1)).toBe(renewed);
    expect(storage.value()).toBe(renewed);
    expect(await session.getAccessToken()).toBe("renewed");
  });

  it("stays signed in when the app opens without a connection", async () => {
    const stored = tokenSet();
    now = signedInAt + 90 * DAY_MS;
    const { session, storage, changes } = setup(stored, async () => ({ kind: "unavailable" }));

    await session.restore();
    await session.renew();

    expect(changes).toEqual([stored]);
    expect(storage.clear).not.toHaveBeenCalled();
    expect(storage.value()).toBe(stored);
  });
});

describe("an access token that expires in flight mode", () => {
  it("fails the call with a session unavailable error and keeps the tokens", async () => {
    const stored = tokenSet();
    now = stored.expiresAt + 1;
    const { session, storage, changes } = setup(stored, async () => ({ kind: "unavailable" }));
    await session.restore();

    await expect(session.getAccessToken()).rejects.toBeInstanceOf(SessionUnavailableError);

    expect(storage.clear).not.toHaveBeenCalled();
    expect(storage.value()).toBe(stored);
    expect(changes).toEqual([stored]);
  });

  it("tries again on the next call and continues once the connection is back", async () => {
    const stored = tokenSet();
    now = stored.expiresAt + 1;
    const renewed = tokenSet({ accessToken: "renewed", expiresAt: now + 300_000 });
    const outcomes: RefreshOutcome[] = [{ kind: "unavailable" }, { kind: "refreshed", tokens: renewed }];
    const { session, refresh } = setup(stored, async () => outcomes.shift()!);
    await session.restore();

    await expect(session.getAccessToken()).rejects.toBeInstanceOf(SessionUnavailableError);
    expect(await session.getAccessToken()).toBe("renewed");
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  it("uses a valid access token without a refresh", async () => {
    const stored = tokenSet();
    const { session, refresh } = setup(stored, async () => ({ kind: "unavailable" }));
    await session.restore();

    expect(await session.getAccessToken()).toBe("access");
    expect(refresh).not.toHaveBeenCalled();
  });
});

describe("a suspended or deleted account", () => {
  it("is signed out when the app opens", async () => {
    const { session, storage, changes } = setup(tokenSet(), async () => ({ kind: "refused" }));
    await session.restore();

    await session.renew();

    expect(changes.at(-1)).toBeNull();
    expect(storage.value()).toBeNull();
    expect(await session.getAccessToken()).toBeUndefined();
  });

  it("is signed out when a call needs a new access token", async () => {
    const stored = tokenSet();
    now = stored.expiresAt + 1;
    const { session, storage, changes } = setup(stored, async () => ({ kind: "refused" }));
    await session.restore();

    expect(await session.getAccessToken()).toBeUndefined();
    expect(changes.at(-1)).toBeNull();
    expect(storage.value()).toBeNull();
  });
});

describe("the token session", () => {
  it("shares one refresh between concurrent calls", async () => {
    const stored = tokenSet();
    now = stored.expiresAt + 1;
    let resolve!: (outcome: RefreshOutcome) => void;
    const { session, refresh } = setup(stored, () => new Promise((r) => (resolve = r)));
    await session.restore();

    const first = session.getAccessToken();
    const second = session.getAccessToken();
    await vi.waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    resolve({ kind: "refreshed", tokens: tokenSet({ accessToken: "renewed", expiresAt: now + 300_000 }) });

    expect(await first).toBe("renewed");
    expect(await second).toBe("renewed");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("keeps a sign-out that happens while a refresh runs", async () => {
    const stored = tokenSet();
    now = stored.expiresAt + 1;
    let resolve!: (outcome: RefreshOutcome) => void;
    const { session, storage, changes } = setup(stored, () => new Promise((r) => (resolve = r)));
    await session.restore();

    const pending = session.getAccessToken();
    await vi.waitFor(() => expect(resolve).toBeDefined());
    expect(await session.signOut()).toBe(stored);
    resolve({ kind: "refreshed", tokens: tokenSet({ accessToken: "late" }) });

    expect(await pending).toBeUndefined();
    expect(storage.value()).toBeNull();
    expect(changes.at(-1)).toBeNull();
  });

  it("stores and announces the tokens of a sign-in", async () => {
    const { session, storage, changes } = setup(null, async () => ({ kind: "unavailable" }));
    expect(await session.restore()).toBeNull();
    const signedIn = tokenSet({ accessToken: "fresh" });

    await session.signIn(signedIn);

    expect(storage.value()).toBe(signedIn);
    expect(changes).toEqual([null, signedIn]);
    expect(await session.getAccessToken()).toBe("fresh");
  });

  it("starts signed out when the stored tokens cannot be read", async () => {
    const { session, storage, changes } = setup(null, async () => ({ kind: "unavailable" }));
    vi.mocked(storage.get).mockRejectedValueOnce(new Error("keystore locked"));

    expect(await session.restore()).toBeNull();
    expect(changes).toEqual([null]);
  });

  it("renews nothing while signed out", async () => {
    const { session, refresh } = setup(null, async () => ({ kind: "refused" }));
    await session.restore();

    await session.renew();

    expect(refresh).not.toHaveBeenCalled();
  });
});
