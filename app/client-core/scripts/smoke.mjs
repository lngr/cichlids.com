#!/usr/bin/env node
// Exercises the generated client against a running Cichlids.Api instance.
// Not a substitute for the Vitest suite: this only proves the generated
// types and the runtime HTTP client agree with a real server response.
import { createCichlidsClient } from "../dist/index.js";

const baseUrl = process.env.CICHLIDS_API_URL ?? "http://localhost:5045";
const client = createCichlidsClient({ baseUrl });

console.log(`Smoke test against ${baseUrl}`);

const list = await client.pictures.list({ limit: 2 });
console.log(`pictures.list -> total=${list.total}, items=${list.items.length}`);
for (const item of list.items) {
  console.log(`  - ${item.slug}: "${item.title}" by ${item.author.displayName ?? item.author.username}`);
}

if (list.items.length === 0) {
  throw new Error("Expected at least one picture from pictures.list");
}

const firstSlug = list.items[0].slug;
const detail = await client.pictures.get(firstSlug);
console.log(`pictures.get("${firstSlug}") -> title="${detail.title}", views=${detail.viewCount}, rating=${detail.ratingAverage}`);

console.log("Smoke test passed.");
