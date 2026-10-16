// Builds the prompts of the pipeline roles (generator, checkers, tie-breakers, adjudicator) and
// of the group sync step. The role texts are agent instructions: they stay English and ASCII.
//
// buildPrompt returns the prompt of one role for one species. The TOOLS paragraph depends on the
// runner: Codex agents use their own web search tool, Claude agents use WebSearch.
// pendingFeatures turns a tally.yaml (parsed) into the tie-breaker feature list that
// buildPrompt expects as "pending".
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, "..", "..");

const TOOLS = {
  codex:
    "Your tools differ from those named in common.md: use your own web search tool where common.md names WebSearch. " +
    "Fetch every page you read with `node tools/species-data/archive-fetch.mjs <url>` as common.md says, never with curl. " +
    "Everything else in common.md applies, including the project tools `literature-search.mjs`, `fetch-oa.mjs`, `crc.mjs` " +
    "and `archive-search.mjs`, the archived references, the access rules and the copyright rules.",
  claude:
    "Use WebSearch for discovery as common.md says. " +
    "Fetch every page you read with `node tools/species-data/archive-fetch.mjs <url>` and cite the printed reference as " +
    "`archived` in the source entry, never with curl or WebFetch. " +
    "Everything else in common.md applies, including the project tools `literature-search.mjs`, `fetch-oa.mjs`, `crc.mjs` " +
    "and `archive-search.mjs`, the archived references, the access rules and the copyright rules.",
};

const METHOD = {
  a: "",
  b: " Method: field data first. Start from scientific papers, field observations and FishBase ecology and diet tables, then consult hobby sources. Read every page you cite in full; values from search-result summaries alone are not evidence.",
  c: " Method: sources first. Collect at least six independent sources (catalog, literature with DOI, species databases, care sheets) before deciding any value, and record which source states which value in `evidence`.",
};

const INDEPENDENCE =
  " Work independently: never read generator.yaml, the other checkers' files, any tiebreaker or tally file, any other file " +
  "of this work directory outside your own output and temporary directory, or the profile or work directory of any other species; " +
  "your result rests on your own research.";

// Tie-breaker features of a parsed tally: [[name, [proposed values...]], ...].
export function pendingFeatures(tally) {
  return (tally.pendingTiebreakers || []).map((n) => [n, (tally.features[n].proposals || []).map((p) => p.value)]);
}

function rules({ root, today, model, validatorNote }) {
  return (
    "Give every network command a timeout. Do not set browser user agents. " +
    "Full texts only via `fetch-oa.mjs`; when it cannot get a full text, use the abstract and never download the paper another way. " +
    "Keep helper scripts in your temporary directory, never under `tools/`. " +
    "Never kill processes you did not start yourself, and leave no background processes running when you finish. " +
    `\`data/species/work\` is a symlink into another checkout, so write every file by its absolute path under ${root}. ` +
    "Change no file of the repository except the ones named here. Do not run git commands that change state. " +
    (validatorNote ? "The validator may report warnings in other files; only your own file has to validate. " : "") +
    "In YAML, write keys without quotes (`id:`, `features:`) and quote string values that contain commas or colons. " +
    `Today is ${today}. In \`provenance.runs\` use model \`${model}\`. ` +
    "Write in English, never copy text from sources, no em or en dashes."
  );
}

export function buildPrompt({
  role,
  root = REPO_ROOT,
  legacyId,
  name,
  hint,
  lake = "",
  legacyIds,
  note = "",
  model,
  runner,
  today = new Date().toISOString().slice(0, 10),
  pending,
}) {
  if (!TOOLS[runner]) throw new Error(`unknown runner: ${runner}`);
  if (!model) throw new Error("model is required");
  const id = String(legacyId);
  const ids = legacyIds || id;
  const tools = TOOLS[runner];
  const water = lake
    ? `Lake ${lake}: the waterbody table decides the natural water values; give the tolerance of each water feature.`
    : "Not a lake of the waterbody table: determine the natural water values (temperature, pH, GH, KH) from field measurements in its native range, and the tolerance of each water feature.";
  const wd = `data/species/work/legacy-${id}`;
  const own = (tmp) =>
    "Do not read anything under `data/species/work/` or `data/species/profiles/` except your own output file, " +
    `your temporary directory \`${wd}/${tmp}/\` and the shared full-text library \`data/species/work/.literature/\`.`;
  const species = `legacy catalog entry ${id} "${name}" (${hint}). Determine the valid name through the Catalog of Fishes. legacyIds: [${ids}]. ${water}`;
  const workIn = `Work in ${root} (a git worktree; run every command from there).`;
  const validate = `node tools/species-data/validate.mjs ${wd}/${role}.yaml`;

  if (role === "generator") {
    const r = rules({ root, today, model, validatorNote: true });
    return (
      `You are the generator of the species profile pipeline of cichlids.com. ${workIn}\n\n` +
      "Read first, completely: `tools/species-data/prompts/common.md` and `tools/species-data/prompts/generator.md`, then the files they name (rubric, schema, hybrid groups, waterbodies table).\n\n" +
      `${tools}\n\n` +
      `Species: ${species} ${note}\n\n${own("tmp-generator")} ${r}\n\n` +
      `Write the profile to \`${root}/${wd}/generator.yaml\` and validate it with \`${validate}\` until it reports 0 errors. Finish with a short report of the hard feature values, the tolerances and your confidence.`
    );
  }
  if (role === "checker-a" || role === "checker-b" || role === "checker-c") {
    const letter = role.slice(-1);
    const r = rules({ root, today, model, validatorNote: true });
    return (
      `You are independent checker ${letter.toUpperCase()} of the species profile pipeline of cichlids.com. ${workIn}\n\n` +
      "Read first, completely: `tools/species-data/prompts/common.md` and `tools/species-data/prompts/checker.md`, then the files they name (rubric, schema, hybrid groups, waterbodies table).\n\n" +
      `${tools}\n\n` +
      `Species: ${species}${METHOD[letter]}${INDEPENDENCE} ${note}\n\n${own("tmp-" + role)} ${r}\n\n` +
      `Write your result to \`${root}/${wd}/${role}.yaml\` and validate it with \`${validate}\` until it reports 0 errors. Finish with a short report of the hard feature values, the tolerances and your confidence.`
    );
  }
  if (role.startsWith("tiebreaker-")) {
    if (!Array.isArray(pending)) throw new Error("pending is required for a tiebreaker role");
    const n = role.split("-")[1];
    const lines = pending
      .map(([f, vals]) => `- ${f}: votes ${vals.map((v) => JSON.stringify(v)).join(", ")}.`)
      .join("\n");
    const r = rules({ root, today, model, validatorNote: false });
    return (
      `You are tie-breaker ${n} of the species profile pipeline of cichlids.com. ${workIn}\n\n` +
      "Read first, completely: `tools/species-data/prompts/common.md` and `tools/species-data/prompts/tiebreaker.md`, then `data/species/rubric.md` (including the section Hybrid groups), `data/species/schema/species-profile.schema.json`, `data/species/hybrid-groups.yaml` and `data/species/waterbodies.yaml`.\n\n" +
      `${tools}\n\n` +
      `Species: legacy catalog entry ${id} "${name}" (${hint}); use the valid name from the Catalog of Fishes. ${water}\n\n` +
      `Named features (votes of the earlier agents, in no order of weight; a name ending in .tolerance means the tolerance of that water feature):\n${lines}\n\n` +
      "Research each named feature from scratch and decide its value by the rubric. For hybridGroups use the ids of `hybrid-groups.yaml` where a listed group fits; propose a new id only for a group the file does not list and state its members and basis in the rationale. " +
      "Write predation as an integer, speciesOnly as a boolean, diet as `{type, bloatSensitive}`, hybridGroups as a list of ids, ranges as `{min, max}` and tolerances as `features.<feature>.tolerance`.\n\n" +
      `${own("tmp-" + role)} Never read the other agents' files of this species. ${r}\n\n` +
      `Write a file that contains only \`features\` with the named features (value, confidence, rationale, sources, evidence) and \`provenance.runs\` with one entry of role \`checker\`, to \`${root}/${wd}/${role}.yaml\`, and check that it parses: ` +
      `\`node -e "require('${root}/tools/species-data/node_modules/yaml').parse(require('fs').readFileSync('${wd}/${role}.yaml','utf8'))"\`. Finish with a short report of your values.`
    );
  }
  if (role === "adjudicator") {
    const r = rules({ root, today, model, validatorNote: false });
    return (
      `You are the adjudicator of the species profile pipeline of cichlids.com. ${workIn}\n\n` +
      "Read first, completely: `tools/species-data/prompts/common.md` and `tools/species-data/prompts/adjudicator.md`, then `data/species/rubric.md`, `data/species/schema/species-profile.schema.json`, `data/species/hybrid-groups.yaml` and the sections Tally and Evidence of `tools/species-data/README.md`.\n\n" +
      `${tools}\n\n` +
      `Species: legacy catalog entry ${id} "${name}" (${hint}). Inputs: every file in \`${wd}/\` (generator.yaml, checker-a.yaml, checker-b.yaml, checker-c.yaml, every tiebreaker-N.yaml and tally.yaml), but not the \`tmp-*\` directories of other roles. Your temporary directory is \`${wd}/tmp-adjudicator/\`. Do not read other species' profiles or work directories. ${note}\n\n` +
      "Rules that have proven themselves: take every hard feature, natural water value and tolerance exactly as `tally.yaml` decides (value, verification, confidence); the natural khDkh carries no verification object. Explain every conservative or median decision in its rationale and name all proposals; when a decided range is narrower than the sources, name the wider source range. " +
      "Cite the lake studies behind `waterbodies.yaml`, never the table file itself. Entries in `provenance.disagreements` list proposals as `{by: <run>, value: <value>}`. Wikipedia, retailer pages and general hobby pages are kind `website`. " +
      "For every group id in the decided hybridGroups that `hybrid-groups.yaml` does not list, add the group (id, description, members, basis documented or presumed by the rubric, sources, userNote that elaborates for hobbyists what was reported, by whom and how reliable it is); for listed groups add the species to the members if missing and change nothing else in them (no source kinds, titles or texts). Change only those group entries and keep the file's plain key style (unquoted keys such as `id:`). " +
      `${r}\n\n` +
      `Write the final profile to \`${root}/data/species/profiles/<id>.yaml\` (the file name equals the profile id). If that file exists already, another legacy entry maps to the same species: never change or overwrite it; write your profile to \`${root}/${wd}/adjudicator-profile.yaml\` instead, validate it there, and start your report with the words PROFILE EXISTS. Run \`node tools/species-data/validate.mjs data/species/profiles/*.yaml\` until it reports 0 errors. Finish with a short report: file name, decided hard features, tolerances, notable decisions, groups added or changed.`
    );
  }
  throw new Error(`unknown role: ${role}`);
}

// Prompt of the group sync step; "list" is the text that names the profiles and groups.
export function buildGroupsyncPrompt({ root = REPO_ROOT, list }) {
  const template = readFileSync(join(HERE, "prompts", "groupsync.md"), "utf8");
  return template.replaceAll("{{ROOT}}", () => root).replaceAll("{{LIST}}", () => list);
}
