import { createDeviceAuth, type DeviceCredential } from "@cichlids/client-core";
import { API_BASE_URL } from "../api/client";
import { localStore } from "../lib/localStore";
import { createDeviceAccount, type DeviceAccount } from "./deviceAccount";
import type { DestroyableKeyProvider } from "./nativeKeyProvider";

const CREDENTIAL_KEY = "cichlids.device.credential";

// The app stops waiting for the first registration after this time and works as a visitor; the attempt continues.
const FIRST_ATTEMPT_PATIENCE_MS = 6_000;
const RETRY_DELAYS_MS = [5_000, 15_000, 60_000, 300_000] as const;

function parseCredential(text: string | null): DeviceCredential | undefined {
  if (!text) return undefined;
  try {
    const value = JSON.parse(text) as Partial<DeviceCredential>;
    return typeof value.credentialId === "string" && typeof value.profileId === "number"
      ? { credentialId: value.credentialId, profileId: value.profileId }
      : undefined;
  } catch {
    return undefined;
  }
}

const credentials = {
  async load() { return parseCredential(await localStore.getItem(CREDENTIAL_KEY)); },
  async save(credential: DeviceCredential) { await localStore.setItem(CREDENTIAL_KEY, JSON.stringify(credential)); },
  async clear() { await localStore.removeItem(CREDENTIAL_KEY); },
};

/** The device account of this installation on top of the given key provider. */
export function assembleDeviceAccount(keys: DestroyableKeyProvider): DeviceAccount {
  return createDeviceAccount({
    createAuth: () => createDeviceAuth({ baseUrl: API_BASE_URL, keys, store: credentials }),
    credentials,
    flags: {
      get: (key) => localStore.getItem(key),
      set: (key, value) => localStore.setItem(key, value),
      remove: (key) => localStore.removeItem(key),
    },
    destroyKey: () => keys.destroy(),
    firstAttemptPatienceMs: FIRST_ATTEMPT_PATIENCE_MS,
    retryDelaysMs: RETRY_DELAYS_MS,
  });
}
