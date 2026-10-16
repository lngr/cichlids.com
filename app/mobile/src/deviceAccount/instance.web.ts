import type { DeviceAccount } from "./deviceAccount";
import { assembleDeviceAccount } from "./assemble";
import { TEST_KEYS_OPT_IN_STORAGE_KEY, deviceAccountMode } from "./config";

function optedIn(): boolean {
  try {
    return globalThis.localStorage?.getItem(TEST_KEYS_OPT_IN_STORAGE_KEY) === "1";
  } catch {
    return false;
  }
}

function createInstance(): DeviceAccount | null {
  // The bundler replaces the variable with its value at build time and drops the branch, so a
  // build without the flag does not contain the test key provider.
  if (process.env.EXPO_PUBLIC_DEVICE_TEST_KEYS === "1" && deviceAccountMode({ os: "web", testKeysBuilt: true, optedIn: optedIn() }) === "test") {
    const { createWebTestKeyProvider } = require("./webTestKeys") as typeof import("./webTestKeys");
    return assembleDeviceAccount(createWebTestKeyProvider());
  }
  return null;
}

/**
 * The web app has no device account. A build of the write E2E stack contains a test key provider
 * that a browser enables with an entry in its localStorage; every other build yields null.
 */
export const deviceAccount: DeviceAccount | null = createInstance();
