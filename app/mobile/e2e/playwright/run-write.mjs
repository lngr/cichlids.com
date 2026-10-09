#!/usr/bin/env node
// Runs every `*.write.mjs` Playwright spec in this directory sequentially against the isolated
// write stack (see e2e/write-stack.sh), which exports E2E_API_URL/E2E_WEB_URL/E2E_KEYCLOAK_URL/
// E2E_DB for the specs to use. Sequential, not parallel, because the specs share one write
// database and one bucket. Succeeds when no spec is found.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const specs = fs
  .readdirSync(dir)
  .filter((name) => name.endsWith(".write.mjs"))
  .sort();

if (specs.length === 0) {
  console.log("No *.write.mjs specs found, nothing to run.");
  process.exit(0);
}

for (const spec of specs) {
  console.log(`Running ${spec}...`);
  await import(path.join(dir, spec));
  console.log(`${spec} passed.`);
}

console.log(`All ${specs.length} write spec(s) passed.`);
