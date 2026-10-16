import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { resolveAppConfig } from "../config/appConfig";
import { TEST_KEYS_FLAG, TEST_KEYS_OPT_IN_STORAGE_KEY, deviceAccountMode, testKeysBuilt } from "./config";

describe("the build-time flag of the test key provider", () => {
  it("is off without a value and for anything but 1", () => {
    expect(testKeysBuilt(undefined)).toBe(false);
    expect(testKeysBuilt("")).toBe(false);
    expect(testKeysBuilt("0")).toBe(false);
    expect(testKeysBuilt("true")).toBe(false);
    expect(testKeysBuilt("1")).toBe(true);
  });

  it("is off in the build of this test run", async () => {
    const { TEST_KEYS_BUILT } = await import("./config");
    expect(TEST_KEYS_BUILT).toBe(false);
  });

  it("makes every build outside development fail", () => {
    const env = { [TEST_KEYS_FLAG]: "1", EXPO_PUBLIC_API_URL: "https://api.example.org", EXPO_PUBLIC_KEYCLOAK_URL: "https://auth.example.org" };
    expect(() => resolveAppConfig({ ...env, APP_VARIANT: "production" })).toThrow(new RegExp(TEST_KEYS_FLAG));
    expect(() => resolveAppConfig({ ...env, APP_VARIANT: "staging" })).toThrow(new RegExp(TEST_KEYS_FLAG));
    expect(() => resolveAppConfig({ [TEST_KEYS_FLAG]: "1" })).not.toThrow();
  });

  it("appears in no build definition of the repository except the script of the write E2E stack", () => {
    const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "..");
    const mobile = join(root, "app", "mobile");
    const files = [
      join(mobile, "Dockerfile"),
      join(mobile, "eas.json"),
      join(mobile, "app.json"),
      join(mobile, "package.json"),
      ...trackedFilesBelow(root, ["infra", ".github/workflows"]),
    ].filter(existsSync);
    expect(files.length).toBeGreaterThan(5);
    const offenders = files.filter((file) => readFileSync(file, "utf8").includes(TEST_KEYS_FLAG));
    expect(offenders).toEqual([]);
    expect(readFileSync(join(mobile, "e2e", "write-stack.sh"), "utf8")).toContain(`${TEST_KEYS_FLAG}=1`);
  });
});

// The versioned files only: the working tree also holds local caches such as provider binaries.
function trackedFilesBelow(root: string, folders: string[]): string[] {
  return execFileSync("git", ["ls-files", "-z", "--", ...folders], { cwd: root, encoding: "utf8" })
    .split("\0")
    .filter((file) => file.length > 0)
    .map((file) => join(root, file));
}

describe("deviceAccountMode", () => {
  it("is native on the phones, whatever the flag says", () => {
    expect(deviceAccountMode({ os: "ios", testKeysBuilt: false, optedIn: false })).toBe("native");
    expect(deviceAccountMode({ os: "android", testKeysBuilt: true, optedIn: true })).toBe("native");
  });

  it("is off on the web", () => {
    expect(deviceAccountMode({ os: "web", testKeysBuilt: false, optedIn: false })).toBe("off");
    expect(deviceAccountMode({ os: "web", testKeysBuilt: false, optedIn: true })).toBe("off");
  });

  it("is the test provider on the web only for a build with the flag and a browser that opted in", () => {
    expect(deviceAccountMode({ os: "web", testKeysBuilt: true, optedIn: false })).toBe("off");
    expect(deviceAccountMode({ os: "web", testKeysBuilt: true, optedIn: true })).toBe("test");
  });

  it("names the storage key of the browser opt-in", () => {
    expect(TEST_KEYS_OPT_IN_STORAGE_KEY).toBe("cichlids.deviceAccount.testKeys");
  });
});
