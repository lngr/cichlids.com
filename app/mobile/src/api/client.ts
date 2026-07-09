import Constants from "expo-constants";
import { createCichlidsClient } from "@cichlids/client-core";

// Resolution order: EXPO_PUBLIC_API_URL (inlined at build time, works in all
// environments incl. EAS builds) falls back to app.json's expo.extra.apiUrl,
// then to the local Cichlids.Api dev port from launchSettings.json.
const apiUrl =
  process.env.EXPO_PUBLIC_API_URL ??
  (Constants.expoConfig?.extra?.apiUrl as string | undefined) ??
  "http://localhost:5045";

export const apiClient = createCichlidsClient({ baseUrl: apiUrl });
export const API_BASE_URL = apiUrl;
