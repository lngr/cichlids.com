#!/usr/bin/env node
// Erzwingt die in ADR-0015 festgelegte, bidirektionale Verknuepfung Story <-> E2E-Test
// als Teil des harten CI-Gates (ADR-0003, ADR-0005): Eine Story mit Status "Done" muss
// mindestens eine existierende E2E-Testdatei referenzieren, die ihrerseits einen Rueck-Tag
// mit der Story-ID traegt. Fehlt die Bindung, schlaegt der Check fehl und blockiert den Merge.
//
// Reines Node ohne externe Abhaengigkeiten, damit der Check ohne Installationsschritt laeuft.
import { readdirSync, readFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

function stripQuotes(s) {
  return s.replace(/^['"]|['"]$/g, "");
}

// Minimaler Frontmatter-Parser fuer die von Backlog.md erzeugten Felder
// (Skalare sowie einfache Block-/Flow-Listen). Liefert mindestens references als Array.
export function parseFrontmatter(content) {
  const fm = { references: [] };
  const m = content.match(/^---\n([\s\S]*?)\n---/);
  if (!m) return fm;
  let listKey = null;
  for (const line of m[1].split("\n")) {
    const item = line.match(/^\s+-\s+(.*)$/);
    if (item && listKey) {
      fm[listKey].push(stripQuotes(item[1].trim()));
      continue;
    }
    const kv = line.match(/^([A-Za-z_]+):\s*(.*)$/);
    if (!kv) continue;
    const key = kv[1];
    const val = kv[2].trim();
    if (val === "") {
      fm[key] = Array.isArray(fm[key]) ? fm[key] : [];
      listKey = key;
    } else if (val === "[]") {
      fm[key] = [];
      listKey = null;
    } else if (val.startsWith("[") && val.endsWith("]")) {
      fm[key] = val.slice(1, -1).split(",").map((s) => stripQuotes(s.trim())).filter(Boolean);
      listKey = null;
    } else {
      fm[key] = stripQuotes(val);
      listKey = null;
    }
  }
  if (!Array.isArray(fm.references)) {
    fm.references = fm.references ? [fm.references] : [];
  }
  return fm;
}

// Prueft alle Stories unter <root>/backlog/tasks. References werden gegen <root> aufgeloest.
export function checkAll({ root }) {
  const failures = [];
  const tasksDir = join(root, "backlog", "tasks");
  let files;
  try {
    files = readdirSync(tasksDir).filter((f) => f.endsWith(".md"));
  } catch {
    return { failures };
  }
  for (const file of files) {
    const fm = parseFrontmatter(readFileSync(join(tasksDir, file), "utf8"));
    if (fm.status !== "Done") continue;
    const id = fm.id ?? file;
    const refs = fm.references ?? [];
    if (refs.length === 0) {
      failures.push(`Story ${id} (Done) hat keine References auf einen E2E-Test (ADR-0015).`);
      continue;
    }
    const existing = refs.filter((r) => existsSync(join(root, r)));
    if (existing.length === 0) {
      failures.push(`Story ${id} (Done): referenzierte Testdatei(en) nicht gefunden: ${refs.join(", ")}.`);
      continue;
    }
    const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const tagRe = new RegExp(`\\b${escaped}\\b`);
    const backlinked = existing.some((r) => tagRe.test(readFileSync(join(root, r), "utf8")));
    if (!backlinked) {
      failures.push(
        `Story ${id} (Done): keine referenzierte Testdatei traegt den Rueck-Tag ${id} (bidirektionale Bindung fehlt).`,
      );
    }
  }
  return { failures };
}

const invokedDirectly =
  process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (invokedDirectly) {
  const root = process.argv[2] ?? process.cwd();
  const { failures } = checkAll({ root });
  if (failures.length > 0) {
    console.error("Story<->Test-Bindung verletzt (Definition of Done, ADR-0015):");
    for (const f of failures) console.error(`  - ${f}`);
    process.exit(1);
  }
  console.log("Story<->Test-Bindung: alle 'Done'-Stories sind korrekt mit einem E2E-Test verknuepft.");
}
