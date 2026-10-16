import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { createCichlidsClient } from "./client.js";
import {
  DeviceAuthError,
  createDeviceAuth,
  deviceSignatureMessage,
  toBase64Url,
} from "./deviceAuth.js";
import type { DeviceCredential, DeviceCredentialStore } from "./deviceAuth.js";
import { createWebCryptoKeyProvider } from "./deviceAuthWebCrypto.js";

const baseUrl = "http://device-auth-mock.test";
const credentialId = "0c2a3f3e-5b55-4d33-9a59-5a8f0f6d2c11";

type Seen = { registrations: string[]; challenges: string[]; exchanges: Record<string, unknown>[]; authorization: (string | null)[] };

let seen: Seen;
let issuedAt = 0;
let lifetimeMs = 15 * 60 * 1000;
let tokenCounter = 0;
let refuseNext: { status: number; code: string } | null = null;
let registrationFails = false;

const fromBase64Url = (text: string): Uint8Array => new Uint8Array(Buffer.from(text, "base64url"));

const server = setupServer(
  http.post(`${baseUrl}/api/auth/device/credentials`, async ({ request }) => {
    if (registrationFails) return HttpResponse.json({ code: "device_key_invalid", detail: "The key is invalid." }, { status: 400 });
    const body = (await request.json()) as { publicKey: string };
    seen.registrations.push(body.publicKey);
    return HttpResponse.json({ credentialId, profileId: 42, displayName: "user12345678" }, { status: 201 });
  }),
  http.post(`${baseUrl}/api/auth/device/challenges`, () => {
    const challenge = toBase64Url(new Uint8Array(32).fill(seen.challenges.length + 1));
    seen.challenges.push(challenge);
    return HttpResponse.json({ challenge, expiresAt: new Date(issuedAt + 120_000).toISOString() });
  }),
  http.post(`${baseUrl}/api/auth/device/tokens`, async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    seen.exchanges.push(body);
    if (refuseNext) {
      const refusal = refuseNext;
      refuseNext = null;
      return HttpResponse.json({ code: refusal.code, detail: "Refused." }, { status: refusal.status });
    }
    tokenCounter += 1;
    return HttpResponse.json({
      accessToken: `device-token-${tokenCounter}`,
      tokenType: "Bearer",
      expiresAt: new Date(issuedAt + lifetimeMs).toISOString(),
    });
  }),
  http.get(`${baseUrl}/api/me`, ({ request }) => {
    seen.authorization.push(request.headers.get("Authorization"));
    return HttpResponse.json({ profile: { id: 42 }, roles: [], provisional: true });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterAll(() => server.close());
beforeEach(() => {
  seen = { registrations: [], challenges: [], exchanges: [], authorization: [] };
  issuedAt = Date.parse("2026-10-16T12:00:00Z");
  lifetimeMs = 15 * 60 * 1000;
  tokenCounter = 0;
  refuseNext = null;
  registrationFails = false;
});
afterEach(() => server.resetHandlers());

function memoryStore(initial?: DeviceCredential): DeviceCredentialStore & { saved: DeviceCredential[] } {
  let value = initial;
  const saved: DeviceCredential[] = [];
  return {
    saved,
    async load() {
      return value;
    },
    async save(credential) {
      value = credential;
      saved.push(credential);
    },
  };
}

describe("the WebCrypto key provider", () => {
  it("creates a P-256 key pair whose private key cannot be exported", async () => {
    const keys = createWebCryptoKeyProvider();
    expect(await keys.hasKey()).toBe(false);
    await keys.generate();
    expect(await keys.hasKey()).toBe(true);

    const spki = await keys.exportPublicKey();
    const publicKey = await crypto.subtle.importKey("spki", spki, { name: "ECDSA", namedCurve: "P-256" }, true, ["verify"]);
    expect(publicKey.algorithm).toMatchObject({ name: "ECDSA", namedCurve: "P-256" });
  });

  it("signs with a signature of 64 bytes that the public key verifies", async () => {
    const keys = createWebCryptoKeyProvider();
    await keys.generate();
    const message = new TextEncoder().encode("hello");

    const signature = await keys.sign(message);

    expect(signature.length).toBe(64);
    const publicKey = await crypto.subtle.importKey("spki", await keys.exportPublicKey(), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    expect(await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, signature, message)).toBe(true);
    expect(await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, signature, new TextEncoder().encode("other"))).toBe(false);
  });

  it("keeps the key pair when generate is called again", async () => {
    const keys = createWebCryptoKeyProvider();
    await keys.generate();
    const first = await keys.exportPublicKey();
    await keys.generate();
    expect(await keys.exportPublicKey()).toEqual(first);
  });

  it("refuses to sign before a key exists", async () => {
    await expect(createWebCryptoKeyProvider().sign(new Uint8Array([1]))).rejects.toThrow(/generate/);
  });

  it("does not allow exporting the private key", async () => {
    const subtle = crypto.subtle;
    let exported = false;
    const spy = {
      generateKey: subtle.generateKey.bind(subtle),
      exportKey: async (format: KeyFormat, key: CryptoKey) => {
        if (!key.extractable) exported = true;
        return subtle.exportKey(format as "spki", key);
      },
      sign: subtle.sign.bind(subtle),
    } as unknown as SubtleCrypto;
    const keys = createWebCryptoKeyProvider(spy);
    await keys.generate();
    await keys.exportPublicKey();
    expect(exported).toBe(false);
  });
});

describe("the signed message", () => {
  it("has the label, challenge, credential id and timestamp on separate lines", () => {
    const text = new TextDecoder().decode(deviceSignatureMessage("abc", credentialId, 1790000000));
    expect(text).toBe(`cichlids-device-auth-v1\nabc\n${credentialId}\n1790000000`);
  });

  it.each([
    [[], ""],
    [[0xff], "_w"],
    [[0xfb, 0xff], "-_8"],
    [[1, 2, 3], "AQID"],
    [[104, 105], "aGk"],
  ])("encodes %j as base64url %s", (bytes, expected) => {
    expect(toBase64Url(new Uint8Array(bytes))).toBe(expected);
  });
});

describe("createDeviceAuth", () => {
  it("registers the key, signs the challenge and returns the token", async () => {
    const keys = createWebCryptoKeyProvider();
    const store = memoryStore();
    const auth = createDeviceAuth({ baseUrl, keys, store, now: () => issuedAt });

    expect(await auth.getAccessToken()).toBe("device-token-1");

    expect(seen.registrations).toEqual([toBase64Url(await keys.exportPublicKey())]);
    expect(store.saved).toEqual([{ credentialId, profileId: 42 }]);
    const exchange = seen.exchanges[0]!;
    expect(exchange.credentialId).toBe(credentialId);
    expect(exchange.challenge).toBe(seen.challenges[0]);
    expect(exchange.timestamp).toBe(Math.floor(issuedAt / 1000));
    const publicKey = await crypto.subtle.importKey("spki", await keys.exportPublicKey(), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const message = deviceSignatureMessage(exchange.challenge as string, credentialId, exchange.timestamp as number);
    expect(
      await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, fromBase64Url(exchange.signature as string) as BufferSource, message as BufferSource),
    ).toBe(true);
  });

  it("caches the token until shortly before it expires", async () => {
    let clock = issuedAt;
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => clock });

    expect(await auth.getAccessToken()).toBe("device-token-1");
    clock = issuedAt + 13 * 60 * 1000;
    expect(await auth.getAccessToken()).toBe("device-token-1");
    expect(seen.exchanges).toHaveLength(1);
  });

  it("signs a new challenge when the token expires within the renewal margin", async () => {
    let clock = issuedAt;
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => clock });
    await auth.getAccessToken();

    clock = issuedAt + 14 * 60 * 1000 + 30_000;
    issuedAt = clock;

    expect(await auth.getAccessToken()).toBe("device-token-2");
    expect(seen.exchanges).toHaveLength(2);
    expect(seen.exchanges[1]!.challenge).not.toBe(seen.exchanges[0]!.challenge);
    expect(seen.registrations).toHaveLength(1);
  });

  it("renews a token that has expired", async () => {
    let clock = issuedAt;
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => clock });
    await auth.getAccessToken();

    clock = issuedAt + 20 * 60 * 1000;
    issuedAt = clock;

    expect(await auth.getAccessToken()).toBe("device-token-2");
  });

  it("shares one exchange between parallel callers", async () => {
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });

    const tokens = await Promise.all([auth.getAccessToken(), auth.getAccessToken(), auth.getAccessToken()]);

    expect(tokens).toEqual(["device-token-1", "device-token-1", "device-token-1"]);
    expect(seen.registrations).toHaveLength(1);
    expect(seen.challenges).toHaveLength(1);
  });

  it("signs a new challenge after invalidate", async () => {
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    await auth.getAccessToken();

    auth.invalidate();

    expect(await auth.getAccessToken()).toBe("device-token-2");
  });

  it("does not register again when the store holds the credential", async () => {
    const keys = createWebCryptoKeyProvider();
    await keys.generate();
    const auth = createDeviceAuth({ baseUrl, keys, store: memoryStore({ credentialId, profileId: 42 }), now: () => issuedAt });

    await auth.getAccessToken();

    expect(seen.registrations).toHaveLength(0);
    expect(seen.exchanges[0]!.credentialId).toBe(credentialId);
  });

  it("does not create a second key pair for a second registration", async () => {
    const keys = createWebCryptoKeyProvider();
    const auth = createDeviceAuth({ baseUrl, keys, now: () => issuedAt });

    await Promise.all([auth.ensureRegistered(), auth.ensureRegistered()]);

    expect(seen.registrations).toHaveLength(1);
  });

  it("reports a refused exchange with its status and code", async () => {
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    refuseNext = { status: 401, code: "device_auth_refused" };

    const failure = await auth.getAccessToken().catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(DeviceAuthError);
    expect(failure).toMatchObject({ status: 401, code: "device_auth_refused" });
  });

  it("signs again after a failed exchange", async () => {
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    refuseNext = { status: 429, code: "device_rate_limited" };
    await expect(auth.getAccessToken()).rejects.toMatchObject({ status: 429 });

    expect(await auth.getAccessToken()).toBe("device-token-1");
  });

  it("reports a refused registration and stores nothing", async () => {
    const store = memoryStore();
    const auth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), store, now: () => issuedAt });
    registrationFails = true;

    await expect(auth.getAccessToken()).rejects.toMatchObject({ status: 400, code: "device_key_invalid" });

    expect(store.saved).toEqual([]);
  });
});

describe("the API client without a sign-in", () => {
  it("sends the device token when no access token exists", async () => {
    const deviceAuth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    const client = createCichlidsClient({ baseUrl, getAccessToken: () => undefined, deviceAuth });

    await client.me.get();

    expect(seen.authorization).toEqual(["Bearer device-token-1"]);
  });

  it("sends the device token when no token source of a sign-in is given", async () => {
    const deviceAuth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    const client = createCichlidsClient({ baseUrl, deviceAuth });

    await client.me.get();
    await client.me.get();

    expect(seen.authorization).toEqual(["Bearer device-token-1", "Bearer device-token-1"]);
    expect(seen.exchanges).toHaveLength(1);
  });

  it("prefers the access token of the sign-in and makes no device request", async () => {
    const deviceAuth = createDeviceAuth({ baseUrl, keys: createWebCryptoKeyProvider(), now: () => issuedAt });
    const client = createCichlidsClient({ baseUrl, getAccessToken: () => "keycloak-token", deviceAuth });

    await client.me.get();

    expect(seen.authorization).toEqual(["Bearer keycloak-token"]);
    expect(seen.registrations).toHaveLength(0);
    expect(seen.challenges).toHaveLength(0);
  });

  it("sends no token without a token source", async () => {
    const client = createCichlidsClient({ baseUrl });

    await client.me.get();

    expect(seen.authorization).toEqual([null]);
  });
});
