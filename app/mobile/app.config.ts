import { readFileSync } from "node:fs";
import { join } from "node:path";
import { SUPPORTED_LANGUAGES } from "./src/i18n/locales.ts";
import type { ConfigContext, ExpoConfig } from "expo/config";
import { resolveAppConfig } from "./src/config/appConfig.ts";

// Extends app.json with build addresses and localized native permissions.
export default ({ config }: ConfigContext): ExpoConfig => {
  const { variant, apiUrl, keycloakUrl } = resolveAppConfig(process.env);
  const permissions = Object.fromEntries(SUPPORTED_LANGUAGES.map((language) => [language,
    JSON.parse(readFileSync(join(__dirname, "src", "i18n", `${language}.json`), "utf8")).upload.libraryPermission as string,
  ]));
  const cameraPermissions = Object.fromEntries(SUPPORTED_LANGUAGES.map((language) => [language,
    JSON.parse(readFileSync(join(__dirname, "src", "i18n", `${language}.json`), "utf8")).timeLapseCamera.cameraPermission as string,
  ]));
  const plugins = (config.plugins ?? []).map((plugin) =>
    Array.isArray(plugin) && plugin[0] === "expo-image-picker"
      ? ["expo-image-picker", { ...plugin[1], photosPermission: permissions.en }] as [string, object]
      : Array.isArray(plugin) && plugin[0] === "expo-camera"
        ? ["expo-camera", { ...plugin[1], cameraPermission: cameraPermissions.en }] as [string, object]
        : plugin,
  );
  return {
    ...config,
    name: config.name ?? "cichlids",
    slug: config.slug ?? "cichlids-mobile",
    locales: Object.fromEntries(SUPPORTED_LANGUAGES.map((language) => [language, {
      ios: { NSPhotoLibraryUsageDescription: permissions[language], NSCameraUsageDescription: cameraPermissions[language] },
    }])),
    plugins: [...plugins, ["expo-localization", { supportsRTL: true, supportedLocales: [...SUPPORTED_LANGUAGES] }]],
    extra: { ...config.extra, variant, apiUrl, keycloakUrl },
  };
};
