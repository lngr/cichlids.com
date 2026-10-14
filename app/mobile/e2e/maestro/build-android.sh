#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
export EXPO_PUBLIC_API_URL=http://10.0.2.2:5046
export EXPO_PUBLIC_KEYCLOAK_URL=http://localhost:8180
export APP_VARIANT=development
export CI=1 EXPO_NO_TELEMETRY=1

pnpm --filter @cichlids/client-core build
# Prebuild generates native sources; package scripts are owned by the workspace.
package_snapshot="$(mktemp)"
cp package.json "$package_snapshot"
trap 'cp "$package_snapshot" package.json; rm -f "$package_snapshot"' EXIT
pnpm exec expo prebuild --platform android --no-install
node --input-type=module <<'JS'
import fs from "node:fs";
const path = "android/app/build.gradle";
const source = fs.readFileSync(path, "utf8");
if (!source.includes("react {")) throw new Error("React Native Gradle configuration is missing");
// Bundle JavaScript into the debug APK so the installed app starts independently of Metro.
fs.writeFileSync(path, source.replace("react {", "react {\n    debuggableVariants = []"));
JS
if [[ "${1:-}" == "--prebuild-only" ]]; then
  echo "Expo prebuild and embedded debug bundle configuration completed"
  exit 0
fi
cd android
./gradlew :app:assembleDebug --no-daemon -PreactNativeArchitectures=x86_64
