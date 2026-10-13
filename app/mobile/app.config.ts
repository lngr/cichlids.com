import type { ConfigContext, ExpoConfig } from "expo/config";
import { resolveAppConfig } from "./src/config/appConfig.ts";

// Extends app.json with the API and Keycloak addresses of the build. Builds outside development
// fail here when an address is missing (see resolveAppConfig).
export default ({ config }: ConfigContext): ExpoConfig => {
  const { variant, apiUrl, keycloakUrl } = resolveAppConfig(process.env);
  return {
    ...config,
    name: config.name ?? "cichlids",
    slug: config.slug ?? "cichlids-mobile",
    extra: { ...config.extra, variant, apiUrl, keycloakUrl },
  };
};
