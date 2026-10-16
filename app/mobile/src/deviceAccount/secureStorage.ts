import * as SecureStore from "expo-secure-store";
import type { SecretStorage } from "./nativeKeyProvider";

// The value stays on this device: it is excluded from backups and from keychain sync, and the app
// reads it after the first unlock so that a background start can sign.
const OPTIONS: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY };

/** The private key rests in the Keychain on iOS and in Keystore-encrypted storage on Android. */
export const secureStorage: SecretStorage = {
  get: (key) => SecureStore.getItemAsync(key, OPTIONS),
  set: (key, value) => SecureStore.setItemAsync(key, value, OPTIONS),
  delete: (key) => SecureStore.deleteItemAsync(key, OPTIONS),
};
