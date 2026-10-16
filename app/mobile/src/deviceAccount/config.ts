/** The environment variable that builds the test key provider into the web app; only the script of the write E2E stack sets it. */
export const TEST_KEYS_FLAG = "EXPO_PUBLIC_DEVICE_TEST_KEYS";

/** The key in the localStorage of a browser that opts into provisional accounts with the test key provider. */
export const TEST_KEYS_OPT_IN_STORAGE_KEY = "cichlids.deviceAccount.testKeys";

export type DeviceAccountMode = "native" | "test" | "off";

export function testKeysBuilt(value: string | undefined): boolean {
  return value === "1";
}

// The variable is written out in full, since the bundler replaces process.env.EXPO_PUBLIC_* at build time.
export const TEST_KEYS_BUILT = testKeysBuilt(process.env.EXPO_PUBLIC_DEVICE_TEST_KEYS);

/**
 * Whether the app keeps a device account. The phones always do. The web app has none, except in a
 * build of the write E2E stack that contains the test key provider, in a browser that opted in.
 * The opt-in keeps the other specs of the stack on the visitor experience.
 */
export function deviceAccountMode(input: { os: string; testKeysBuilt: boolean; optedIn: boolean }): DeviceAccountMode {
  if (input.os === "ios" || input.os === "android") return "native";
  return input.os === "web" && input.testKeysBuilt && input.optedIn ? "test" : "off";
}
