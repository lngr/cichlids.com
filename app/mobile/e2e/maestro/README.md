# Native Android E2E

The five Maestro flows run against `com.cichlids.app`, the Android package in
`app/mobile/app.json` used by `app.config.ts`. App selectors use React Native test IDs.
Keycloak's browser login accepts English and German accessible labels. Fixture titles
are synthetic content and independent of the UI language.

## CI

The `maestro` job in `.github/workflows/ci.yml` runs on pushes to `main` and manual
workflow dispatches. Its matrix runs every flow on fresh Android API 35
`google_apis;x86_64` emulators with `en-US` and `de-DE` device locales. The runner
sets the system locale, restarts the Android framework and checks the locale property
before installing the APK. Each matrix entry has its own
Postgres, RustFS and Keycloak services, `cichlids_e2e` database and `cichlids-e2e`
bucket. Only synthetic fixtures and the committed test image are used.

`build-android.sh` uses Expo Prebuild and Gradle `:app:assembleDebug`. The generated
React Native Gradle configuration includes JavaScript and assets in the debug APK.
This build runs with the local debug signing key and needs neither Expo credentials
nor a running Metro server. The architecture is limited to `x86_64` for the emulator.
Gradle and the native dependency versions come from the installed Expo template and
the workspace lockfile.

The app reaches the API at `http://10.0.2.2:5046`, and public image URLs use
`http://10.0.2.2:9000/cichlids-e2e`. Keycloak uses `http://localhost:8180` with
`adb reverse tcp:8180 tcp:8180`: the API and app must see the same OIDC issuer.
The stack exposes local development HTTP endpoints, which the debug manifest permits.
The `dev-mod` fixture owns the rating target; `dev-user` publishes uploads and comments.

Maestro is installed at `MAESTRO_MIN` from `tools/toolchain.versions`. Every flow runs
even if an earlier flow fails. The job fails when any flow fails. Artifacts named
`maestro-en-US` and `maestro-de-DE` contain JUnit reports, Maestro diagnostics and
stack logs, with a retention of seven days. The `required` gate requires Maestro on
push and dispatch; a pull request permits the deliberately skipped Maestro job.

## KVM in the devcontainer

The host needs hardware virtualization and `/dev/kvm`. The privileged devcontainer
exposes that device. `.devcontainer/postCreateCommand.sh` resolves its numeric group
and adds the remote user to it, including when an existing group has that GID.

Run **Dev Containers: Rebuild Container**, then close the remote connection and
reopen it so terminal processes receive the updated supplementary groups. In the new
terminal, check:

```bash
id
stat -c '%g %A' /dev/kvm
test -r /dev/kvm && test -w /dev/kvm && echo 'KVM accessible'
```

## Local execution

Use JDK 21, Node 24, the workspace's pnpm version, Android SDK command-line tools,
`adb`, `emulator` and the pinned Maestro version. Set `JAVA_HOME`, `ANDROID_HOME`
and their executable paths. Accept Android SDK licenses with `sdkmanager --licenses`.
Install Maestro from the `cli-${MAESTRO_MIN}` release ZIP, as in the CI job.

From the repository root, create the emulator once:

```bash
sdkmanager 'platform-tools' 'emulator' 'system-images;android-35;google_apis;x86_64'
echo no | avdmanager create avd -n cichlids-e2e \
  -k 'system-images;android-35;google_apis;x86_64' -d pixel_6
pnpm --dir app install --frozen-lockfile
app/mobile/e2e/maestro/build-android.sh
```

For each locale, start a fresh emulator in a separate terminal and wait for its home
screen. The Google APIs image supports `adb root`, which the runner uses to configure
Chrome's first-run behavior.

```bash
emulator -avd cichlids-e2e -no-snapshot -wipe-data
```

Then run from the repository root:

```bash
app/stack/services-up.sh --wait-realm
CICHLIDS_WRITE_PUBLIC_HOST=10.0.2.2 app/mobile/e2e/write-stack.sh up
node app/mobile/e2e/maestro/seed.mjs
MAESTRO_DEVICE_LOCALE=en-US MAESTRO_REPORT_DIR=/tmp/maestro-en-US \
  app/mobile/e2e/maestro/run-android.sh
app/mobile/e2e/write-stack.sh down
adb emu kill
```

Execute the cleanup commands after a failed flow run as well. Repeat the emulator and
stack commands with `de-DE` and `/tmp/maestro-de-DE`. Recreating the isolated stack
resets ratings and uploads. The local compose services can stay running for development.
The commands modify only `cichlids_e2e` and `cichlids-e2e`; bootstrap and legacy imports
are unnecessary.

## Picture-detail negative check

In a temporary test branch, make the picture detail route render only a placeholder
with no `picture-title` element. Build and install that APK, seed the isolated stack,
and run `maestro test app/mobile/e2e/maestro/browse-gallery.yaml`. The flow must fail
at the wait/assertion for `picture-title`. The gallery screen and a generic placeholder
cannot satisfy that assertion. Run the same flow against the full detail route to
verify that it passes. This device check needs KVM and is separate from YAML parsing.
