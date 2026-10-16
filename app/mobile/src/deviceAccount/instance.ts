import { getRandomBytesAsync } from "expo-crypto";
import type { DeviceAccount } from "./deviceAccount";
import { assembleDeviceAccount } from "./assemble";
import { createNativeKeyProvider } from "./nativeKeyProvider";
import { secureStorage } from "./secureStorage";

/** The device account of the native app: a P-256 key in the secure store of the operating system. */
export const deviceAccount: DeviceAccount | null = assembleDeviceAccount(
  createNativeKeyProvider({ storage: secureStorage, randomBytes: getRandomBytesAsync }),
);
