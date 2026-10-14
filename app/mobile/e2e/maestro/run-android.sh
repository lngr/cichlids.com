#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
report_dir="${MAESTRO_REPORT_DIR:-/tmp/cichlids-maestro-reports}"
mkdir -p "$report_dir"
app_id="$(node -p 'require("./app.json").expo.android.package')"

requested_locale="${MAESTRO_DEVICE_LOCALE:-en-US}"
case "$requested_locale" in
  en-US|de-DE) ;;
  *) echo "Unsupported device locale: $requested_locale" >&2; exit 2 ;;
esac
# Restart the Android framework so app resources and Expo localization use this locale.
adb root
adb wait-for-device
adb shell settings put system system_locales "$requested_locale"
adb shell setprop persist.sys.locale "$requested_locale"
adb shell setprop sys.boot_completed 0
adb shell stop
adb shell start
booted=false
for ((i = 0; i < 120; i++)); do
  if [[ "$(adb shell getprop sys.boot_completed | tr -d '\r')" == "1" ]]; then
    booted=true
    break
  fi
  sleep 1
done
if [[ "$booted" != true ]]; then
  echo "Android framework did not finish booting after the locale change" >&2
  exit 1
fi
device_locale="$(adb shell getprop persist.sys.locale | tr -d '\r')"
printf 'Device locale: %s\n' "$device_locale" | tee "$report_dir/device-locale.txt"
if [[ "$device_locale" != "$requested_locale" ]]; then
  echo "Expected device locale $requested_locale, got $device_locale" >&2
  exit 1
fi
adb shell input keyevent 82
adb install -r android/app/build/outputs/apk/debug/app-debug.apk
# The OIDC issuer uses localhost on both the API host and the device.
adb reverse tcp:8180 tcp:8180
# The rooted Google APIs image lets Chrome skip its first-run onboarding.
adb shell 'echo "chrome --disable-fre --no-default-browser-check --no-first-run" > /data/local/tmp/chrome-command-line'
adb logcat -c
failed=0
for flow in browse-gallery tank-detail community-thread login-upload comment-rating; do
  adb shell am force-stop "$app_id"
  adb shell pm clear com.android.chrome >/dev/null
  # Each flow has its own report; failures do not prevent the remaining flows from running.
  if ! maestro test --format junit --output "$report_dir/$flow.xml" \
    --debug-output "$report_dir/$flow" "e2e/maestro/$flow.yaml"; then
    failed=1
  fi
done
adb logcat -d > "$report_dir/logcat.txt"
exit "$failed"
