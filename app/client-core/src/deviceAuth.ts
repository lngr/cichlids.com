/**
 * Authentication of an installation without an account. The installation keeps an ECDSA P-256 key
 * pair, registers the public key with the API and signs a one-time challenge for every device
 * token. The token lives fifteen minutes and has no refresh token: DeviceAuth caches it and signs
 * a new challenge shortly before it expires.
 */

/** The key pair of the installation. The private key never leaves the provider. */
export interface DeviceKeyProvider {
  /** Whether the installation has a key pair. */
  hasKey(): Promise<boolean>;
  /** Creates the key pair of the installation; a provider that holds a key pair keeps it. */
  generate(): Promise<void>;
  /** The public key as DER encoded SubjectPublicKeyInfo. */
  exportPublicKey(): Promise<Uint8Array>;
  /** Signs the message with ECDSA over P-256 and SHA-256; the signature is the 64 bytes r and s (IEEE P1363). */
  sign(message: Uint8Array): Promise<Uint8Array>;
}

/** The identifiers the API assigned to the registered key. */
export interface DeviceCredential {
  credentialId: string;
  profileId: number;
}

/** Keeps the credential of the installation across app starts. */
export interface DeviceCredentialStore {
  load(): Promise<DeviceCredential | undefined>;
  save(credential: DeviceCredential): Promise<void>;
}

export interface DeviceToken {
  accessToken: string;
  /** The expiry in milliseconds since 1970-01-01T00:00:00Z. */
  expiresAt: number;
}

export interface DeviceAuthOptions {
  /** Base URL of the Cichlids.Api instance. */
  baseUrl: string;
  keys: DeviceKeyProvider;
  /** Keeps the credential; the credential is lost with the app's memory when omitted. */
  store?: DeviceCredentialStore;
  fetch?: typeof fetch;
  /** The current time in milliseconds; for tests. */
  now?: () => number;
  /** A cached token is renewed when it expires within this many milliseconds. Default 60 seconds. */
  renewBeforeMs?: number;
}

/**
 * A challenge signed by the installation, the body that proves possession of the device key. The
 * API accepts each proof once and within minutes of its timestamp.
 */
export interface DeviceProof {
  challenge: string;
  credentialId: string;
  /** Whole seconds since 1970-01-01T00:00:00Z. */
  timestamp: number;
  /** The 64 bytes r and s of the signature, base64url. */
  signature: string;
}

export interface DeviceAuth {
  /** The cached token, or a new one signed with the key of the installation. Parallel calls share one exchange. */
  getAccessToken(): Promise<string>;
  /** Asks for a fresh challenge and signs it; the proof serves one request, such as handing the content of the installation to an account. */
  signProof(): Promise<DeviceProof>;
  /** Creates the key pair when missing and registers it with the API when the installation has no credential. */
  ensureRegistered(): Promise<DeviceCredential>;
  /** Drops the cached token, so the next call signs a new challenge; for a request the API answered with 401. */
  invalidate(): void;
}

/** A refusal or failure of the device authentication endpoints. */
export class DeviceAuthError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
    message: string,
  ) {
    super(message);
    this.name = "DeviceAuthError";
  }
}

const MESSAGE_LABEL = "cichlids-device-auth-v1";
const DEFAULT_RENEW_BEFORE_MS = 60_000;

/** The bytes the installation signs: label, challenge, credential id and timestamp in seconds, one per line. */
export function deviceSignatureMessage(challenge: string, credentialId: string, timestampSeconds: number): Uint8Array {
  return new TextEncoder().encode(`${MESSAGE_LABEL}\n${challenge}\n${credentialId}\n${timestampSeconds}`);
}

const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

export function toBase64Url(bytes: Uint8Array): string {
  let text = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const chunk = (bytes[i]! << 16) | ((bytes[i + 1] ?? 0) << 8) | (bytes[i + 2] ?? 0);
    text += ALPHABET[(chunk >> 18) & 63]! + ALPHABET[(chunk >> 12) & 63]!;
    if (i + 1 < bytes.length) text += ALPHABET[(chunk >> 6) & 63]!;
    if (i + 2 < bytes.length) text += ALPHABET[chunk & 63]!;
  }
  return text;
}

export function createDeviceAuth(options: DeviceAuthOptions): DeviceAuth {
  const root = options.baseUrl.replace(/\/+$/, "");
  const now = options.now ?? (() => Date.now());
  const renewBefore = options.renewBeforeMs ?? DEFAULT_RENEW_BEFORE_MS;
  const doFetch = (input: string, init: RequestInit) => (options.fetch ?? globalThis.fetch)(input, init);

  let credential: DeviceCredential | undefined;
  let token: DeviceToken | undefined;
  let registering: Promise<DeviceCredential> | undefined;
  let exchanging: Promise<DeviceToken> | undefined;

  async function post<T>(route: string, body: unknown): Promise<T> {
    const response = await doFetch(`${root}${route}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(body),
    });
    const payload = (await response.json().catch(() => null)) as (T & { code?: string; detail?: string }) | null;
    if (!response.ok) {
      throw new DeviceAuthError(response.status, payload?.code ?? null, payload?.detail ?? `The request to ${route} failed with ${response.status}.`);
    }
    return payload as T;
  }

  async function register(): Promise<DeviceCredential> {
    const stored = credential ?? (await options.store?.load());
    if (stored) {
      credential = stored;
      return stored;
    }
    if (!(await options.keys.hasKey())) await options.keys.generate();
    const publicKey = toBase64Url(await options.keys.exportPublicKey());
    const registered = await post<{ credentialId: string; profileId: number }>("/api/auth/device/credentials", { publicKey });
    credential = { credentialId: registered.credentialId, profileId: registered.profileId };
    await options.store?.save(credential);
    return credential;
  }

  async function signProof(): Promise<DeviceProof> {
    const { credentialId } = await ensureRegistered();
    const { challenge } = await post<{ challenge: string }>("/api/auth/device/challenges", {});
    const timestamp = Math.floor(now() / 1000);
    const signature = await options.keys.sign(deviceSignatureMessage(challenge, credentialId, timestamp));
    return { challenge, credentialId, timestamp, signature: toBase64Url(signature) };
  }

  async function exchange(): Promise<DeviceToken> {
    const issued = await post<{ accessToken: string; expiresAt: string }>("/api/auth/device/tokens", await signProof());
    return { accessToken: issued.accessToken, expiresAt: Date.parse(issued.expiresAt) };
  }

  function ensureRegistered(): Promise<DeviceCredential> {
    registering ??= register().finally(() => {
      registering = undefined;
    });
    return registering;
  }

  return {
    ensureRegistered,
    signProof,
    invalidate() {
      token = undefined;
    },
    async getAccessToken() {
      if (token && token.expiresAt - now() > renewBefore) return token.accessToken;
      exchanging ??= exchange()
        .then((issued) => {
          token = issued;
          return issued;
        })
        .finally(() => {
          exchanging = undefined;
        });
      return (await exchanging).accessToken;
    },
  };
}
