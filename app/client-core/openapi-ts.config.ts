// Read by `@hey-api/openapi-ts` (see scripts/generate.mjs). Deliberately not
// `import`ing `defineConfig` from that package: this file also has to load
// inside the isolated codegen sandbox scripts/generate.mjs spins up via
// `pnpm dlx`, where only the CLI binary is resolvable, not the package's own
// module graph. A plain object matching its `UserConfig` shape avoids that
// resolution entirely while remaining valid input to the CLI.
export default {
  input: "openapi.json",
  output: {
    path: "src/generated",
    // Without this, the generator's own tsconfig auto-discovery comes up
    // empty and it falls back to extension-less relative imports, which
    // Node's ESM resolver rejects under this package's "moduleResolution":
    // "NodeNext". An absolute path sidesteps that lookup.
    tsConfigPath: new URL("./tsconfig.json", import.meta.url).pathname,
  },
  plugins: [
    "@hey-api/client-fetch",
    "@hey-api/typescript",
    "@hey-api/sdk",
  ],
};
