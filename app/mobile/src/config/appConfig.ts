export type AppVariant = string;

export interface AppConfig {
  /** The build profile: "development" for local work, otherwise the name of the EAS profile. */
  variant: AppVariant;
  apiUrl: string;
  keycloakUrl: string;
}

const DEVELOPMENT = "development";
const API_URL_VARIABLE = "EXPO_PUBLIC_API_URL";
const KEYCLOAK_URL_VARIABLE = "EXPO_PUBLIC_KEYCLOAK_URL";

// Local Cichlids.Api port from launchSettings.json and the Keycloak port of app/stack/compose.yaml.
const DEVELOPMENT_API_URL = "http://localhost:5045";
const DEVELOPMENT_KEYCLOAK_URL = "http://localhost:8180";

type Environment = Record<string, string | undefined>;

function present(value: string | undefined): string | undefined {
  return value === undefined || value === "" ? undefined : value;
}

/**
 * Resolves the API and Keycloak addresses of a build from its environment. The variant comes from
 * APP_VARIANT or, on EAS, from EAS_BUILD_PROFILE; without either it is "development". Only
 * development falls back to localhost addresses: every other variant needs EXPO_PUBLIC_API_URL
 * and EXPO_PUBLIC_KEYCLOAK_URL and throws an error naming the missing ones.
 */
export function resolveAppConfig(env: Environment): AppConfig {
  const variant = present(env.APP_VARIANT) ?? present(env.EAS_BUILD_PROFILE) ?? DEVELOPMENT;
  const apiUrl = present(env[API_URL_VARIABLE]);
  const keycloakUrl = present(env[KEYCLOAK_URL_VARIABLE]);

  if (variant === DEVELOPMENT) {
    return { variant, apiUrl: apiUrl ?? DEVELOPMENT_API_URL, keycloakUrl: keycloakUrl ?? DEVELOPMENT_KEYCLOAK_URL };
  }

  const missing = [
    apiUrl ? null : API_URL_VARIABLE,
    keycloakUrl ? null : KEYCLOAK_URL_VARIABLE,
  ].filter((name): name is string => name !== null);
  if (!apiUrl || !keycloakUrl) {
    throw new Error(
      `The ${variant} build needs ${missing.join(" and ")}. Set ${missing.length > 1 ? "them" : "it"} in the environment or in the env block of the "${variant}" profile in eas.json.`,
    );
  }
  return { variant, apiUrl, keycloakUrl };
}

/**
 * The address the running app uses: the value inlined from the environment at build time, else the
 * one app.config.ts wrote into the Expo config. Throws with the variable name when both are absent.
 */
export function pickRuntimeUrl(variable: string, inlined: string | undefined, configured: string | undefined): string {
  const url = present(inlined) ?? present(configured);
  if (!url) throw new Error(`${variable} is not configured.`);
  return url;
}
