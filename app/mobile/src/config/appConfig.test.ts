import { describe, expect, it } from "vitest";
import { pickRuntimeUrl, resolveAppConfig } from "./appConfig";

describe("resolveAppConfig", () => {
  it("falls back to the local ports in development", () => {
    expect(resolveAppConfig({})).toEqual({
      variant: "development",
      apiUrl: "http://localhost:5045",
      keycloakUrl: "http://localhost:8180",
    });
  });

  it("treats the development variant like an unset one and prefers explicit addresses", () => {
    const config = resolveAppConfig({
      APP_VARIANT: "development",
      EXPO_PUBLIC_API_URL: "http://localhost:5046",
      EXPO_PUBLIC_KEYCLOAK_URL: "http://192.168.1.5:8180",
    });
    expect(config).toEqual({ variant: "development", apiUrl: "http://localhost:5046", keycloakUrl: "http://192.168.1.5:8180" });
  });

  it("uses the EAS build profile when no variant is set", () => {
    expect(() => resolveAppConfig({ EAS_BUILD_PROFILE: "production" })).toThrow(/EXPO_PUBLIC_API_URL/);
  });

  it("names the missing API address outside development", () => {
    expect(() => resolveAppConfig({ APP_VARIANT: "staging", EXPO_PUBLIC_KEYCLOAK_URL: "https://auth.example.org" })).toThrow(
      /EXPO_PUBLIC_API_URL/,
    );
  });

  it("names the missing Keycloak address outside development", () => {
    expect(() => resolveAppConfig({ APP_VARIANT: "production", EXPO_PUBLIC_API_URL: "https://api.example.org" })).toThrow(
      /EXPO_PUBLIC_KEYCLOAK_URL/,
    );
  });

  it("names every missing address and the variant", () => {
    expect(() => resolveAppConfig({ APP_VARIANT: "staging" })).toThrow(/staging.*EXPO_PUBLIC_API_URL.*EXPO_PUBLIC_KEYCLOAK_URL/s);
  });

  it("treats an empty address as missing", () => {
    expect(() => resolveAppConfig({ APP_VARIANT: "staging", EXPO_PUBLIC_API_URL: "", EXPO_PUBLIC_KEYCLOAK_URL: "https://auth.example.org" })).toThrow(
      /EXPO_PUBLIC_API_URL/,
    );
  });

  it("takes the addresses outside development from the environment", () => {
    const config = resolveAppConfig({
      APP_VARIANT: "staging",
      EXPO_PUBLIC_API_URL: "https://api.staging.cichlids.com",
      EXPO_PUBLIC_KEYCLOAK_URL: "https://auth.staging.cichlids.com",
    });
    expect(config).toEqual({
      variant: "staging",
      apiUrl: "https://api.staging.cichlids.com",
      keycloakUrl: "https://auth.staging.cichlids.com",
    });
  });
});

describe("pickRuntimeUrl", () => {
  it("prefers the inlined environment value over the config value", () => {
    expect(pickRuntimeUrl("EXPO_PUBLIC_API_URL", "https://a.example.org", "https://b.example.org")).toBe("https://a.example.org");
  });

  it("uses the config value when the environment has none", () => {
    expect(pickRuntimeUrl("EXPO_PUBLIC_API_URL", undefined, "https://b.example.org")).toBe("https://b.example.org");
  });

  it("throws with the variable name when neither exists", () => {
    expect(() => pickRuntimeUrl("EXPO_PUBLIC_KEYCLOAK_URL", undefined, undefined)).toThrow(/EXPO_PUBLIC_KEYCLOAK_URL/);
  });
});
