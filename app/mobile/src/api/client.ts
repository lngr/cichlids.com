import Constants from "expo-constants";
import { createCichlidsClient } from "@cichlids/client-core";
import { pickRuntimeUrl } from "../config/appConfig";

// EXPO_PUBLIC_API_URL is inlined at build time; app.config.ts writes the build's resolved address
// into expo.extra.apiUrl (localhost only in development).
const apiUrl = pickRuntimeUrl("EXPO_PUBLIC_API_URL", process.env.EXPO_PUBLIC_API_URL, Constants.expoConfig?.extra?.apiUrl as string | undefined);

type AccessTokenProvider = () => Promise<string | undefined>;

let accessTokenProvider: AccessTokenProvider | null = null;

/** Sets the source of the bearer token for API calls; with null every call is anonymous. */
export function setAccessTokenProvider(provider: AccessTokenProvider | null): void {
  accessTokenProvider = provider;
}

export const apiClient = createCichlidsClient({
  baseUrl: apiUrl,
  getAccessToken: () => accessTokenProvider?.(),
});
export const API_BASE_URL = apiUrl;
