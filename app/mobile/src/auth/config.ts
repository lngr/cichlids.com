import Constants from "expo-constants";
import { pickRuntimeUrl } from "../config/appConfig";

// EXPO_PUBLIC_KEYCLOAK_URL is inlined at build time; app.config.ts writes the build's resolved
// address into expo.extra.keycloakUrl (localhost only in development).
const keycloakUrl = pickRuntimeUrl(
  "EXPO_PUBLIC_KEYCLOAK_URL",
  process.env.EXPO_PUBLIC_KEYCLOAK_URL,
  Constants.expoConfig?.extra?.keycloakUrl as string | undefined,
);

const realm = "cichlids";

/** OIDC settings of the app's public Keycloak client. */
export const authConfig = {
  keycloakUrl,
  realm,
  issuer: `${keycloakUrl}/realms/${realm}`,
  clientId: "cichlids-app",
  // offline_access makes the session an offline session that lasts until the member signs out.
  scopes: ["openid", "profile", "email", "offline_access"],
  redirectScheme: "cichlids",
  redirectPath: "auth",
} as const;
