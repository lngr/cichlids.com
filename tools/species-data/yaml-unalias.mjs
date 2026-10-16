// Expands YAML anchors and aliases in place, keeping the text layout of every other line.
// Handled forms: anchors on list items ("- &name" with a block below), anchors on mapping values
// ("key: &name" with a block below or an inline value such as "[]", also after "- "), and
// aliases as list items ("- *name") or as mapping values ("key: *name", also after "- ").
//
// Usage: node tools/species-data/yaml-unalias.mjs <file.yaml>...
//
// Each file is rewritten only when the expanded text parses to the same value as the original.
// A mismatch or a failure leaves the file unchanged and makes the exit code 1. The files that
// changed are printed.
import { readFileSync, writeFileSync } from "node:fs";
import { isDeepStrictEqual } from "node:util";
import { pathToFileURL } from "node:url";
import { parse } from "yaml";

const NAME = "[^\\s,\\[\\]{}]+";
// indent, optional "- ", optional "key:", anchor name, optional inline value
const ANCHOR_LINE = new RegExp(`^( *)(- )?(?:([A-Za-z0-9_.-]+):)? *&(${NAME})(?: +(\\S.*))?$`);
const ALIAS_LINE = new RegExp(`^( *)(- )?(?:([A-Za-z0-9_.-]+):)? *\\*(${NAME}) *$`);

// "key: [elements]" on one line; elements that are aliases or anchors turn the line into a block list.
const FLOW_LINE = /^( *)(- )?([A-Za-z0-9_.-]+): \[(.*)\]\s*$/;

// Splits flow sequence content at top-level commas, outside quotes, braces and brackets.
function splitFlow(text) {
  const parts = [];
  let depth = 0;
  let quote = null;
  let start = 0;
  for (let k = 0; k < text.length; k += 1) {
    const c = text[k];
    if (quote) {
      if (c === "\\" && quote === '"') k += 1;
      else if (c === quote) quote = null;
    } else if (c === '"' || c === "'") quote = c;
    else if (c === "{" || c === "[") depth += 1;
    else if (c === "}" || c === "]") depth -= 1;
    else if (c === "," && depth === 0) {
      parts.push(text.slice(start, k).trim());
      start = k + 1;
    }
  }
  parts.push(text.slice(start).trim());
  return parts.filter((p) => p !== "");
}

const indentOf = (line) => line.length - line.trimStart().length;
const pad = (n) => " ".repeat(n);

// Expands the anchors and aliases of the lines. Bodies are stored without their leading
// indentation, with their own nested aliases already expanded, so an alias refers to the
// closest preceding anchor of its name.
function expandLines(lines, anchors) {
  const out = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    const flow = FLOW_LINE.exec(line);
    if (flow) {
      const elements = splitFlow(flow[4]);
      if (elements.some((e) => /^[&*]/.test(e))) {
        const [, indent, dash, key] = flow;
        const itemIndent = pad(indent.length + (dash ? 2 : 0) + 2);
        out.push(`${indent}${dash ?? ""}${key}:`);
        for (const element of elements) {
          const ref = /^([&*])(\S+)(?: +(\S.*))?$/.exec(element);
          let inline = element;
          if (ref && ref[1] === "*") {
            const found = anchors.get(ref[2]);
            if (!found) throw new Error(`alias *${ref[2]} on line ${i + 1} has no anchor`);
            out.push(...(found.inline === undefined ? renderItem(itemIndent, found.body) : [`${itemIndent}- ${found.inline}`]));
            continue;
          }
          if (ref) {
            if (ref[3] === undefined) throw new Error(`anchor &${ref[2]} on line ${i + 1} has no value`);
            anchors.set(ref[2], { body: [], inline: ref[3] });
            inline = ref[3];
          }
          out.push(`${itemIndent}- ${inline}`);
        }
        i += 1;
        continue;
      }
    }
    const anchor = ANCHOR_LINE.exec(line);
    if (anchor && (anchor[2] || anchor[3])) {
      const [, indent, dash, key, name, inline] = anchor;
      // Block lines must sit deeper than the key (or the dash content column for "- &name").
      const keyCol = indent.length + (dash ? 2 : 0);
      const minIndent = key ? keyCol + 1 : indent.length + 2;
      // A list under a mapping key may sit at the key's own column.
      const sameColumnItem = (l) => key && indentOf(l) === keyCol && /^- |^-$/.test(l.trimStart());
      let j = i + 1;
      while (j < lines.length && lines[j].trim() !== "" && (indentOf(lines[j]) >= minIndent || sameColumnItem(lines[j]))) j += 1;
      const raw = lines.slice(i + 1, j);
      if (raw.length === 0 && inline === undefined) {
        throw new Error(`anchor &${name} on a line without a value: ${line.trim()}`);
      }
      const base = raw.length ? Math.min(...raw.map(indentOf)) : 0;
      const body = expandLines(raw.map((l) => l.slice(base)), anchors);
      anchors.set(name, { body, inline });
      if (key) {
        out.push(`${indent}${dash ?? ""}${key}:${inline === undefined ? "" : ` ${inline}`}`);
        out.push(...body.map((l) => (l === "" ? l : pad(base) + l)));
      } else if (inline !== undefined) {
        out.push(`${indent}- ${inline}`);
      } else {
        out.push(...renderItem(indent, body));
      }
      i = j;
      continue;
    }
    const alias = ALIAS_LINE.exec(line);
    if (alias && (alias[2] || alias[3])) {
      const [, indent, dash, key, name] = alias;
      const found = anchors.get(name);
      if (!found) throw new Error(`alias *${name} on line ${i + 1} has no anchor`);
      if (!key) {
        out.push(...(found.inline === undefined ? renderItem(indent, found.body) : [`${indent}- ${found.inline}`]));
      } else if (found.inline !== undefined) {
        out.push(`${indent}${dash ?? ""}${key}: ${found.inline}`);
      } else {
        const keyCol = indent.length + (dash ? 2 : 0);
        out.push(`${indent}${dash ?? ""}${key}:`);
        out.push(...found.body.map((l) => (l === "" ? l : pad(keyCol + 2) + l)));
      }
    } else {
      out.push(line);
    }
    i += 1;
  }
  return out;
}

// Lays a block (lines relative to the item content column) out as a list item at indent.
function renderItem(indent, body) {
  return [`${indent}- ${body[0]}`, ...body.slice(1).map((line) => `${indent}  ${line}`)];
}

export function expandAliases(text) {
  return expandLines(text.split("\n"), new Map()).join("\n");
}

function main(paths) {
  let failed = false;
  for (const path of paths) {
    try {
      const original = readFileSync(path, "utf8");
      const expanded = expandAliases(original);
      if (expanded === original) continue;
      if (!isDeepStrictEqual(parse(expanded), parse(original))) {
        throw new Error("expanded text parses differently from the original");
      }
      writeFileSync(path, expanded);
      console.log(path);
    } catch (error) {
      failed = true;
      console.error(`${path}: ${error.message}`);
    }
  }
  return failed ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const paths = process.argv.slice(2);
  if (paths.length === 0) {
    console.error("Usage: yaml-unalias.mjs <file.yaml>...");
    process.exit(2);
  }
  process.exit(main(paths));
}
