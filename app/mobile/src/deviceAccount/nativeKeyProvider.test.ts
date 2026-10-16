import { webcrypto } from "node:crypto";
import { describe, expect, it, vi } from "vitest";
import { createNativeKeyProvider, type SecretStorage } from "./nativeKeyProvider";

const { subtle } = webcrypto;

function memoryStorage(): SecretStorage & { values: Map<string, string> } {
  const values = new Map<string, string>();
  return {
    values,
    async get(key) { return values.get(key) ?? null; },
    async set(key, value) { values.set(key, value); },
    async delete(key) { values.delete(key); },
  };
}

const fixedRandom = (...chunks: Uint8Array[]) => {
  const queue = [...chunks];
  return vi.fn(async (length: number) => {
    const next = queue.shift();
    if (!next) throw new Error("no more random bytes");
    expect(next.length).toBe(length);
    return next;
  });
};

const importPublicKey = (spki: Uint8Array) => subtle.importKey("spki", Buffer.from(spki), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);

const secret = (fill: number) => new Uint8Array(32).fill(fill);

describe("the key provider of the native app", () => {
  it("has no key before it generates one", async () => {
    const keys = createNativeKeyProvider({ storage: memoryStorage(), randomBytes: fixedRandom() });
    expect(await keys.hasKey()).toBe(false);
    await expect(keys.sign(new Uint8Array([1]))).rejects.toThrow(/no device key/i);
    await expect(keys.exportPublicKey()).rejects.toThrow(/no device key/i);
  });

  it("exports the public key as SubjectPublicKeyInfo that WebCrypto accepts", async () => {
    const keys = createNativeKeyProvider({ storage: memoryStorage(), randomBytes: fixedRandom(secret(7)) });
    await keys.generate();
    const imported = await importPublicKey(await keys.exportPublicKey());
    expect(imported.type).toBe("public");
  });

  it("signs with ECDSA over P-256 and SHA-256 in the 64 byte IEEE P1363 form that the public key verifies", async () => {
    const keys = createNativeKeyProvider({ storage: memoryStorage(), randomBytes: fixedRandom(secret(9)) });
    await keys.generate();
    const message = new TextEncoder().encode("cichlids-device-auth-v1\nchallenge\ncredential\n1760000000");
    const signature = await keys.sign(message);
    expect(signature.length).toBe(64);
    const publicKey = await importPublicKey(await keys.exportPublicKey());
    expect(await subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, Buffer.from(signature), Buffer.from(message))).toBe(true);
    expect(await subtle.verify({ name: "ECDSA", hash: "SHA-256" }, publicKey, Buffer.from(signature), Buffer.from("other"))).toBe(false);
  });

  it("keeps the key in the storage, so a new provider of the same installation signs with it", async () => {
    const storage = memoryStorage();
    const first = createNativeKeyProvider({ storage, randomBytes: fixedRandom(secret(3)) });
    await first.generate();
    const second = createNativeKeyProvider({ storage, randomBytes: fixedRandom() });
    expect(await second.hasKey()).toBe(true);
    expect(Array.from(await second.exportPublicKey())).toEqual(Array.from(await first.exportPublicKey()));
  });

  it("keeps an existing key when asked to generate again", async () => {
    const storage = memoryStorage();
    const random = fixedRandom(secret(3), secret(4));
    const keys = createNativeKeyProvider({ storage, randomBytes: random });
    await keys.generate();
    const before = Array.from(await keys.exportPublicKey());
    await keys.generate();
    expect(Array.from(await keys.exportPublicKey())).toEqual(before);
    expect(random).toHaveBeenCalledTimes(1);
  });

  it("draws again when the random bytes are no valid private key", async () => {
    const keys = createNativeKeyProvider({ storage: memoryStorage(), randomBytes: fixedRandom(secret(0), secret(5)) });
    await keys.generate();
    expect(await keys.hasKey()).toBe(true);
  });

  it("treats a damaged stored value as no key", async () => {
    const storage = memoryStorage();
    storage.values.set("cichlids.device.key", "not a key");
    const keys = createNativeKeyProvider({ storage, randomBytes: fixedRandom(secret(6)) });
    expect(await keys.hasKey()).toBe(false);
    await keys.generate();
    expect(await keys.hasKey()).toBe(true);
  });

  it("never writes the private key to the log", async () => {
    const storage = memoryStorage();
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((name) => vi.spyOn(console, name).mockImplementation(() => {}));
    const keys = createNativeKeyProvider({ storage, randomBytes: fixedRandom(secret(8)) });
    await keys.generate();
    await keys.sign(new Uint8Array([1, 2, 3]));
    await keys.exportPublicKey();
    for (const spy of spies) {
      expect(spy).not.toHaveBeenCalled();
      spy.mockRestore();
    }
  });

  it("deletes the key on destroy, so the next generate makes a different one", async () => {
    const storage = memoryStorage();
    const keys = createNativeKeyProvider({ storage, randomBytes: fixedRandom(secret(2), secret(3)) });
    await keys.generate();
    const first = Array.from(await keys.exportPublicKey());
    await keys.destroy();
    expect(await keys.hasKey()).toBe(false);
    await keys.generate();
    expect(Array.from(await keys.exportPublicKey())).not.toEqual(first);
  });

  it("offers no way to read the private key", async () => {
    const keys = createNativeKeyProvider({ storage: memoryStorage(), randomBytes: fixedRandom(secret(2)) });
    expect(Object.keys(keys).sort()).toEqual(["destroy", "exportPublicKey", "generate", "hasKey", "sign"]);
  });
});
