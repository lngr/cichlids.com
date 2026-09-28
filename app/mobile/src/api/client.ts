import Constants from "expo-constants";
import { createCichlidsClient } from "@cichlids/client-core";

// Resolution order: EXPO_PUBLIC_API_URL (inlined at build time, works in all
// environments incl. EAS builds) falls back to app.json's expo.extra.apiUrl,
// then to the local Cichlids.Api dev port from launchSettings.json.
const apiUrl =
  process.env.EXPO_PUBLIC_API_URL ??
  (Constants.expoConfig?.extra?.apiUrl as string | undefined) ??
  "http://localhost:5045";

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
