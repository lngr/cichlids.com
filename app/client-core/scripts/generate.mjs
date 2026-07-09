#!/usr/bin/env node
// Fetches the OpenAPI document from a running Cichlids.Api instance, writes a
// checked-in copy to openapi.json, and regenerates src/generated/ from that
// copy via @hey-api/openapi-ts, driven by openapi-ts.config.ts.
import { rm, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { execFileSync } from "node:child_process";

const packageRoot = fileURLToPath(new URL("..", import.meta.url));
const apiUrl = process.env.CICHLIDS_API_URL ?? "http://localhost:5045";
const openapiUrl = `${apiUrl}/openapi/v1.json`;
const openapiJsonPath = path.join(packageRoot, "openapi.json");
const generatedOutPath = path.join(packageRoot, "src/generated");

console.log(`Fetching OpenAPI document from ${openapiUrl} ...`);
const response = await fetch(openapiUrl);
if (!response.ok) {
  throw new Error(`Failed to fetch OpenAPI document: ${response.status} ${response.statusText}`);
}
const document = await response.json();
await writeFile(openapiJsonPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
console.log(`Wrote ${path.relative(packageRoot, openapiJsonPath)}`);

// @hey-api/openapi-ts reads ts.SyntaxKind and friends off the host's
// `typescript` package at codegen time. The workspace pins typescript@7.0.2
// (the native-preview compiler), which does not expose that API surface, so
// codegen runs in an isolated pnpm dlx environment carrying its own
// typescript@5 instead of the workspace's tsc. This does not affect the tsc
// used for app typecheck/build.
await rm(generatedOutPath, { recursive: true, force: true });
execFileSync(
  "pnpm",
  [
    "dlx",
    "--package", "typescript@^5.9.0",
    "--package", "@hey-api/openapi-ts@0.99.0",
    "openapi-ts",
  ],
  { stdio: "inherit", cwd: packageRoot },
);
console.log(`Wrote ${path.relative(packageRoot, generatedOutPath)}`);
