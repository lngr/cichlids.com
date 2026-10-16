import { p256 } from "@noble/curves/nist.js";
import { bytesToHex, hexToBytes } from "@noble/curves/utils.js";
import type { DeviceKeyProvider } from "@cichlids/client-core";

/** A key provider whose key can be deleted, which a registration does once the device account is gone. */
export type DestroyableKeyProvider = DeviceKeyProvider & { destroy(): Promise<void> };

/** Where the private key rests: the secure store of the operating system. */
export interface SecretStorage {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  delete(key: string): Promise<void>;
}

export const DEVICE_KEY_STORAGE_KEY = "cichlids.device.key";

// DER prefix of a SubjectPublicKeyInfo for an uncompressed P-256 point: SEQUENCE, the algorithm
// identifier (id-ecPublicKey, prime256v1) and the BIT STRING header of the 65 byte point.
const SPKI_PREFIX = hexToBytes("3059301306072a8648ce3d020106082a8648ce3d030107034200");

const SECRET_KEY_LENGTH = 32;

export interface NativeKeyProviderOptions {
  storage: SecretStorage;
  /** Cryptographically secure random bytes of the given length. */
  randomBytes(length: number): Promise<Uint8Array>;
}

/**
 * The key pair of the native installation: an ECDSA P-256 private key in the secure store of the
 * operating system (Keychain on iOS, Keystore-encrypted storage on Android) and signatures from
 * the audited @noble/curves implementation, since the Expo runtime has no ECDSA primitive. The
 * store keeps the key readable by the app on this device only. Signing and the public key are
 * the only operations; the private key is never logged, returned or sent anywhere, and is
 * loaded into memory for the duration of one call.
 *
 * A key that lives inside the secure hardware (Secure Enclave, StrongBox) and cannot be read out
 * at all needs a native module and is a provider of its own behind the same interface.
 */
export function createNativeKeyProvider(options: NativeKeyProviderOptions): DestroyableKeyProvider {
  async function loadSecretKey(): Promise<Uint8Array | null> {
    const stored = await options.storage.get(DEVICE_KEY_STORAGE_KEY);
    if (!stored || !/^[0-9a-f]{64}$/.test(stored)) return null;
    const secretKey = hexToBytes(stored);
    return p256.utils.isValidSecretKey(secretKey) ? secretKey : null;
  }

  async function requireSecretKey(): Promise<Uint8Array> {
    const secretKey = await loadSecretKey();
    if (!secretKey) throw new Error("The installation has no device key yet; call generate() first.");
    return secretKey;
  }

  return {
    async hasKey() {
      return (await loadSecretKey()) !== null;
    },
    async generate() {
      if (await loadSecretKey()) return;
      // A random value is a valid P-256 private key with overwhelming probability; the check
      // covers the remaining range (zero and values from the group order up).
      for (;;) {
        const candidate = await options.randomBytes(SECRET_KEY_LENGTH);
        if (p256.utils.isValidSecretKey(candidate)) {
          await options.storage.set(DEVICE_KEY_STORAGE_KEY, bytesToHex(candidate));
          return;
        }
      }
    },
    async destroy() {
      await options.storage.delete(DEVICE_KEY_STORAGE_KEY);
    },
    async exportPublicKey() {
      const point = p256.getPublicKey(await requireSecretKey(), false);
      const spki = new Uint8Array(SPKI_PREFIX.length + point.length);
      spki.set(SPKI_PREFIX);
      spki.set(point, SPKI_PREFIX.length);
      return spki;
    },
    async sign(message) {
      // SHA-256 of the message, then ECDSA with a deterministic nonce; the result is r and s as 64 bytes.
      return p256.sign(message, await requireSecretKey(), { prehash: true, format: "compact" });
    },
  };
}
