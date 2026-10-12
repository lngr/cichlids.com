#!/usr/bin/env node
// Exports the legacy species catalog with photo counts to data/species/legacy-catalog.json.
//
// The export holds catalog data only (names, category, photo counts), no member data. It reads the
// restored legacy MySQL database read-only through its running container.
import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const CONTAINER = "cichlids-legacy-db-1";
const OUTPUT = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "data", "species", "legacy-catalog.json");

const CATEGORIES = {
  1: "Lake Tanganyika",
  2: "Lake Malawi",
  3: "American",
  4: "South American",
  5: "Victoria",
  6: "African",
  7: "Community",
  8: "Central American",
  9: "South American",
};

const QUERY = `
SELECT s.uid, g.title, n.title, COALESCE(c.title, ''), s.category,
       (SELECT COUNT(DISTINCT m.uid_local)
          FROM user_cichlids_species_pictures_mm m
          JOIN user_cichlids_pictures p ON p.uid = m.uid_local
         WHERE m.uid_foreign = s.uid AND p.deleted = 0 AND p.hidden = 0)
  FROM user_cichlids_species s
  JOIN user_cichlids_genus_names g ON g.uid = s.genus
  JOIN user_cichlids_species_names n ON n.uid = s.species
  LEFT JOIN user_cichlids_common_name c ON c.uid = s.common
 WHERE s.deleted = 0 AND s.hidden = 0
 ORDER BY s.uid`;

const stdout = execFileSync(
  "docker",
  ["exec", CONTAINER, "mysql", "-uroot", "-plegacy", "--batch", "--skip-column-names", "cichlids_typo3", "-e", QUERY],
  { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], maxBuffer: 16 * 1024 * 1024 },
);

const species = stdout
  .split("\n")
  .filter((line) => line.length > 0)
  .map((line) => {
    const [uid, genus, name, common, category, photos] = line.split("\t");
    return {
      legacyId: Number(uid),
      genus: genus.trim(),
      species: name.trim(),
      commonName: common.trim() || null,
      category: CATEGORIES[Number(category)] ?? null,
      photos: Number(photos),
    };
  })
  .sort((a, b) => b.photos - a.photos || a.legacyId - b.legacyId);

mkdirSync(dirname(OUTPUT), { recursive: true });
writeFileSync(OUTPUT, JSON.stringify(species, null, 2) + "\n", "utf8");
console.error(`${species.length} species written to ${OUTPUT}`);
