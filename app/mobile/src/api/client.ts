import { Platform } from "react-native";
import Constants from "expo-constants";
import { createCichlidsClient, type DeviceAuth } from "@cichlids/client-core";
import { pickRuntimeUrl } from "../config/appConfig";
import { createDeviceTokenGate } from "./deviceTokenGate";
import { createCookieSessionFetch } from "./sessionFetch";

// EXPO_PUBLIC_API_URL is inlined at build time; app.config.ts writes the build's resolved address
// into expo.extra.apiUrl (localhost only in development).
const apiUrl = pickRuntimeUrl("EXPO_PUBLIC_API_URL", process.env.EXPO_PUBLIC_API_URL, Constants.expoConfig?.extra?.apiUrl as string | undefined);

type AccessTokenProvider = () => Promise<string | undefined>;

let accessTokenProvider: AccessTokenProvider | null = null;
let webSessionCsrfToken: string | null = null;
let webSessionEndedListener: (() => void) | null = null;
let deviceAuthSource: (() => DeviceAuth) | null = null;
let deviceTokensActive = false;
let deviceRefusedListener: (() => void) | null = null;
let registrationRequiredListener: (() => void) | null = null;

/** Sets the source of the bearer token for API calls; with null every call is anonymous. */
export function setAccessTokenProvider(provider: AccessTokenProvider | null): void {
  accessTokenProvider = provider;
}

/** Sets the CSRF token that web requests send with every write; null without a web session. */
export function setWebSessionCsrfToken(token: string | null): void {
  webSessionCsrfToken = token;
}

/** Sets the function the web client calls when the API answers 401, which ends the web session. */
export function setWebSessionEndedListener(listener: (() => void) | null): void {
  webSessionEndedListener = listener;
}

/**
 * Sets where the API client finds the device authentication of the installation. With useTokens,
 * requests that have no access token carry the device token; without it the authentication only
 * signs the proof of a registration. Null removes it.
 */
export function setDeviceAuth(source: (() => DeviceAuth) | null, useTokens: boolean): void {
  deviceAuthSource = source;
  deviceTokensActive = source !== null && useTokens;
}

/** Sets the function called when the API refuses the credential of the installation. */
export function setDeviceRefusedListener(listener: (() => void) | null): void {
  deviceRefusedListener = listener;
}

/** Sets the function called when the API answers a write with registration_required. */
export function setRegistrationRequiredListener(listener: (() => void) | null): void {
  registrationRequiredListener = listener;
}

// The web build authenticates with the httpOnly session cookie the API manages; native builds
// send the bearer token.
const webFetch =
  Platform.OS === "web"
    ? createCookieSessionFetch({
        fetch: (input, init) => fetch(input, init),
        getCsrfToken: () => webSessionCsrfToken,
        onUnauthorized: () => webSessionEndedListener?.(),
      })
    : undefined;

export const apiClient = createCichlidsClient({
  baseUrl: apiUrl,
  getAccessToken: () => accessTokenProvider?.(),
  fetch: webFetch,
  deviceAuth: createDeviceTokenGate({
    getAuth: () => deviceAuthSource?.() ?? null,
    isActive: () => deviceTokensActive,
    onRefused: () => deviceRefusedListener?.(),
  }),
  // The cookie session of the web app authenticates without a bearer token.
  accountSession: () => webSessionCsrfToken !== null,
  onRegistrationRequired: () => registrationRequiredListener?.(),
});
export const API_BASE_URL = apiUrl;
