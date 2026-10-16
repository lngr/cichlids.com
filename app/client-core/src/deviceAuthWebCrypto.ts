import type { DeviceKeyProvider } from "./deviceAuth.js";

/**
 * A key provider on WebCrypto, for tests and for tools that play an installation. The key pair is
 * ECDSA P-256 with a private key that is not extractable and lives in the memory of the provider.
 */
export function createWebCryptoKeyProvider(subtle: SubtleCrypto = globalThis.crypto.subtle): DeviceKeyProvider {
  let pair: CryptoKeyPair | undefined;

  const requirePair = (): CryptoKeyPair => {
    if (!pair) throw new Error("The installation has no device key yet; call generate() first.");
    return pair;
  };

  return {
    async hasKey() {
      return pair !== undefined;
    },
    async generate() {
      pair ??= (await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign", "verify"])) as CryptoKeyPair;
    },
    async exportPublicKey() {
      return new Uint8Array(await subtle.exportKey("spki", requirePair().publicKey));
    },
    async sign(message) {
      return new Uint8Array(await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, requirePair().privateKey, message as BufferSource));
    },
  };
}
