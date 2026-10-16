import type { DestroyableKeyProvider } from "./nativeKeyProvider";

const DATABASE = "cichlids-device-test-keys";
const STORE = "keys";
const PAIR = "pair";

function request<T>(operation: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    operation.onsuccess = () => resolve(operation.result);
    operation.onerror = () => reject(operation.error);
  });
}

async function openStore(): Promise<IDBDatabase> {
  const opening = indexedDB.open(DATABASE, 1);
  opening.onupgradeneeded = () => opening.result.createObjectStore(STORE);
  return request(opening);
}

async function withStore<T>(mode: IDBTransactionMode, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const database = await openStore();
  try {
    return await request(run(database.transaction(STORE, mode).objectStore(STORE)));
  } finally {
    database.close();
  }
}

/**
 * A key provider for the write E2E stack that stands in for the secure store of the phones: an
 * ECDSA P-256 pair from WebCrypto whose private key is not extractable and lives in IndexedDB
 * (structured clone keeps the CryptoKey object, so the key bytes never reach script). Only a build
 * with the test key flag contains it.
 */
export function createWebTestKeyProvider(subtle: SubtleCrypto = globalThis.crypto.subtle): DestroyableKeyProvider {
  const load = () => withStore<CryptoKeyPair | undefined>("readonly", (store) => store.get(PAIR));
  const requirePair = async (): Promise<CryptoKeyPair> => {
    const pair = await load();
    if (!pair) throw new Error("The installation has no device key yet; call generate() first.");
    return pair;
  };
  return {
    async hasKey() {
      return (await load()) !== undefined;
    },
    async generate() {
      if (await load()) return;
      const pair = (await subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, ["sign", "verify"])) as CryptoKeyPair;
      await withStore("readwrite", (store) => store.put(pair, PAIR));
    },
    async exportPublicKey() {
      return new Uint8Array(await subtle.exportKey("spki", (await requirePair()).publicKey));
    },
    async sign(message) {
      return new Uint8Array(await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, (await requirePair()).privateKey, message as BufferSource));
    },
    async destroy() {
      await withStore("readwrite", (store) => store.delete(PAIR));
    },
  };
}
