import Constants from "expo-constants";

// Resolution order: EXPO_PUBLIC_KEYCLOAK_URL (inlined at build time) falls back to app.json's
// expo.extra.keycloakUrl, then to the local Keycloak port of app/stack/compose.yaml.
const keycloakUrl =
  process.env.EXPO_PUBLIC_KEYCLOAK_URL ??
  (Constants.expoConfig?.extra?.keycloakUrl as string | undefined) ??
  "http://localhost:8180";

const realm = "cichlids";

/** OIDC settings of the app's public Keycloak client. */
export const authConfig = {
  keycloakUrl,
  realm,
  issuer: `${keycloakUrl}/realms/${realm}`,
  clientId: "cichlids-app",
  scopes: ["openid", "profile", "email"],
  redirectScheme: "cichlids",
  redirectPath: "auth",
} as const;
