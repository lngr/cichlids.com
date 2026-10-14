# Species data tools

Tooling around the species catalog in `data/species/`.

## Contents

- `literature-search.mjs`: searches scientific literature through OpenAlex (with an API key from `OPENALEX_API_KEY` or `~/.config/cichlids/openalex.key`), Europe PMC and Crossref, throttled across processes, and prints year, title, DOI, open-access link and abstract, so research agents need no web search for literature. Each printed result is cached for 30 days in `data/species/work/.literature/search/<sha1 of query and limit>.txt`; a cached query prints the cached text without network access.
- `source-archive.mjs`: stores internal source snapshots and shares text extraction, access checks and process-safe host throttling with the research fetchers.
- `archive-fetch.mjs`: fetches and archives a public web page or document and prints its reference and readable text.
- `archive-backfill.mjs`: archives unique source URLs from profiles and shared tables and inserts missing archive references without reformatting YAML.
- `fetch-oa.mjs`: fetches the full text of open-access works by DOI into a local library (see Open-access full texts).
- `crc.mjs`: reads the public part of the Cichlid Room Companion for the agents and keeps each page read in the source archive. The site blocks curl and headless browsers through a Cloudflare bot check, so the tool drives a headed Chromium on a virtual display.
- `export-legacy-catalog.mjs`: exports the legacy species catalog with photo counts to `data/species/legacy-catalog.json`. It reads the restored legacy MySQL database read-only through its running container and holds catalog data only.
- `prompts/`: prompts of the agent pipeline (generator, checker, tie-breaker, adjudicator and the shared rules) that produce species profiles.
- `check-seriously-fish.mjs`: checks accepted names and catalog synonyms for archived Seriously Fish care pages and writes resumable decisions to `data/species/seriously-fish.yaml`.
- `build-directory.mjs`: builds the accepted-species directory and legacy mapping from archived catalog and FishBase inputs. `build-directory.test.mjs` holds its tests.
- `validate.mjs`: validator for profile files and the species directory. `validate.test.mjs` holds its tests.
- `source-trust.mjs`: loads and validates `data/species/source-trust.yaml` and resolves source trust by URL or kind. `source-trust.test.mjs` holds its tests.
- `tally.mjs`: counts the votes of the agents of one work directory and decides the hard features that the votes settle. `tally.test.mjs` holds its tests.
- `waterbodies.mjs`: loads and validates the table of measured lake values `data/species/waterbodies.yaml` and finds an entry by id, name or alias. `waterbodies.test.mjs` holds its tests.
- `validate-articles.mjs`: validator for knowledge articles in `data/knowledge/articles/`. `validate-articles.test.mjs` holds its tests.
- `render-review.mjs`: renders profiles into one read-only HTML page with a comparison table, all features with sources and agent disagreements, and a badge with the verification on each hard feature.

## Setup

```
cd tools/species-data
npm ci
```

The export script uses only Node built-ins. The validator needs the dependencies `ajv`, `ajv-formats` and `yaml`, which are pinned in `package.json`. `fetch-oa.mjs` needs `pdfjs-dist` and, for its browser fallback, the same Chromium and `xvfb-run` as `crc.mjs`. `crc.mjs` needs `playwright-core`, a Chromium from the Playwright cache (`~/.cache/ms-playwright/chromium-*`, or the path in `CHROMIUM_PATH`) and `xvfb-run`.

## Export

```
node tools/species-data/export-legacy-catalog.mjs
```

## Species directory and legacy mapping

`data/species/directory.yaml` lets the app list and select every accepted cichlid species,
link to its original description and find a full profile where one exists. It contains one
`species` entry per final `Current status: Valid as` record in the Catalog of Fishes family list.
Historical status statements, uncertain names and records without a final status do not supply
accepted species. The archived list contains 2,375 nominal records, including 1,771 accepted
species and 557 synonym records. A synonym assigned only to a genus supplies no species alias.

The build uses two immutable archive references in `build-directory.mjs`:

- Eschmeyer's Catalog of Fishes, Cichlidae family list:
  `researcharchive.calacademy.org/research/ichthyology/catalog/fishcatget.asp/q-b612c649/2026-10-14T110429Z`.
- FishBase species table v25.04:
  `huggingface.co/datasets/cboettig/fishbase/resolve/main/data/fb/v25.04/parquet/species.parquet/2026-10-14T165859Z`.

`resolveSnapshot` resolves references through the archive root, including
`CICHLIDS_SOURCE_ARCHIVE`. The build reads catalog text and the FishBase binary locally and
performs no network requests. `hyparquet`, pinned to `1.31.4`, reads Parquet through JavaScript.
The archive preserves Parquet bytes in `original.bin` and identifies the dataset in `text.txt`;
header and footer signatures distinguish Parquet from text and error documents.

```sh
node tools/species-data/archive-fetch.mjs https://huggingface.co/datasets/cboettig/fishbase/resolve/main/data/fb/v25.04/parquet/species.parquet
node tools/species-data/build-directory.mjs
npm --prefix tools/species-data run build-directory
node tools/species-data/validate.mjs data/species/profiles/*.yaml
```

The two build commands are equivalent. `--directory-only` writes the directory while preserving
`legacy-mapping.yaml` and `trade-forms.yaml`. Rebuilding sorts species by accepted name and mappings
by numeric `legacyId`, uses stable YAML formatting and retains the source access dates from
snapshot metadata. It prints a JSON report with species and FishBase match totals, inspected
column names, Aquarium values, cichlid Aquarium counts, mapping counts and unresolved legacy
names. FishBase Cichlidae rows have `FamCode: 349`; the report identifies that code through
`Geophagus` rows. The relevant table columns are `SpecCode`, `Genus`, `Species`, `Author`,
`FamCode` and `Aquarium`. The Aquarium value set is `commercial`, `highly commercial`,
`never/rarely`, `potential`, `public aquariums`, `show aquarium` and `null`.

### Directory fields

The document holds `sources` once at file level, with the profile-source fields `kind`, `title`,
`url`, `accessed` and `archived`. Its `species` list contains:

- `id`: accepted name in lowercase kebab-case, matching the profile id convention.
- `name`, `genus`, `epithet`, `author`, `year`: accepted taxonomy, with author names separated
  from the numeric year and combination parentheses.
- `subfamily`, `distribution`, `habitat`: the final catalog status section's values.
- `originalDescription`: `citation` (original authors, year, pages and publication), numeric
  `catalogRef`, reference-page `url`, `originalCombination` (original genus and epithet), and
  `typeLocality`. Author initials in catalog brackets and figure or plate references are omitted
  from the citation; alternate page numbers in brackets and publication qualifiers are retained.
- `synonyms`: original combinations of final synonym records pointing to the accepted species,
  plus its own original combination when different. Names are deduplicated and sorted.
- `links`: accepted-name `catalogOfFishes` species query, `fishbase` summary URL or `null`, and
  `seriouslyFish` species-page URL or `null`.
- `fishbase`: `{specCode, aquarium}` or `null`. Aquarium keeps the raw table value, including
  `null`. Matching tries accepted genus and epithet, then catalog synonym combinations. Multiple
  distinct FishBase candidates leave the match unresolved.
- `trade`: `true` when FishBase Aquarium equals `commercial` or `highly commercial`, or a
  Seriously Fish species page exists. Seriously Fish publishes care pages for species kept by
  hobbyists; these pages supply evidence that FishBase Aquarium alone omits. A completed negative
  Seriously Fish check gives `false` without positive FishBase evidence. An undecided Seriously
  Fish check gives `null` without positive FishBase evidence.
- `profile`: id of the profile with the same `taxonomy.validName`, or `null`.

Absent catalog values remain `null`. Directory stubs contain taxonomy and references;
compatibility and husbandry facts belong to the full profiles.

### Seriously Fish checks

```sh
node tools/species-data/check-seriously-fish.mjs
node tools/species-data/build-directory.mjs --directory-only
node --test tools/species-data/*.test.mjs
node tools/species-data/validate.mjs data/species/profiles/*.yaml
```

`data/species/seriously-fish.yaml` holds a `species` mapping keyed by directory id. Each decided
entry has `url`, `archived` and `checked` (UTC date). A found page supplies its candidate URL and
archive reference; a negative decision has `url: null` and `archived: null`. Missing ids represent
undecided checks. The directory includes the file as a derived database source at file level,
with the Seriously Fish site URL and the latest check date as `accessed`. Individual archive
references belong to the check file.

Candidates use `https://www.seriouslyfish.com/species/<lowercase-hyphenated-name>`, first the
accepted name, then each unique directory synonym. The canonical route omits a trailing slash.
A species title and a canonical species URL identify a species page. Title comparisons ignore
quotation marks around genus and epithet. A title using another genus
also qualifies when the canonical URL matches the candidate, or the canonical URL matches the
title and its epithet matches a candidate name. A matching title with Maintenance and Water
conditions sections also qualifies without a canonical URL. Species pages with sparse husbandry
content count because the selection rule uses page existence. A page naming another accepted
directory species is excluded from the requested species, because synonym binomials can be
homonymous. HTTP 404 and 410 establish absence. Network failures, server errors, access blocks
and unrecognized successful documents leave the candidate undecided. A species receives a
negative decision only when every candidate establishes absence; any found candidate supplies
positive evidence even when another candidate fails.

The check fetches one candidate at a time through `source-archive.mjs`. Its honest research user
agent and shared host throttle enforce a minimum two-second interval between public requests,
including redirects. Successful snapshots are reused. Decisions are written atomically after
each species, so rerunning the command skips decided ids and retries undecided checks. The
command exits with code 1 when undecided species remain.

### Legacy mapping

`data/species/legacy-mapping.yaml` holds `mappings`, one entry for each of the 829 records in
`legacy-catalog.json`. Each entry has `legacyId`, `legacyName` (legacy genus and species),
`target` (directory or trade-form id, or `null`) and `match`, with an optional `note`.

| Match | Meaning |
| --- | --- |
| `profile` | A profile claims the legacy id. Its valid name or trade-form id determines the target. |
| `exact` | The legacy name equals an accepted directory name. |
| `synonym` | The legacy name equals a catalog original combination for that species. |
| `correction` | A manually resolved spelling, gender ending or outdated combination, with the reason in `note`. |
| `trade-form` | A manually identified undescribed aquarium form. |
| `hybrid` | A manually identified hybrid. |
| `domestic-form` | A manually identified domestic form. |
| `open` | No authoritative or unambiguous automatic match, with `target: null`. |

The build generates `profile`, `exact`, `synonym` and `open` in that priority order. It preserves
existing `correction`, `trade-form`, `hybrid` and `domestic-form` entries, including their YAML
comments and values. It makes no spelling corrections. Ambiguous original combinations remain
open unless a profile or a manual entry establishes the target. A preserved entry that
contradicts a profile fails validation.

### Trade forms

`data/species/trade-forms.yaml` holds `forms` for names without an accepted described species.
The build seeds only forms implied by profiles with status `undescribed`, `hybrid` or
`domestic-form`. Each entry contains the profile `id` and `taxonomy.validName` as `name`,
`kind` (`trade-form` for undescribed profiles), `parents` (directory ids from `taxonomy.parents`,
or an empty list), `note` (a curator's explanation, or `null`) and `sources` (the profile's
taxonomy source). Existing forms retain their contents; missing profile-implied forms are
appended. Further forms and mapping corrections require curation supported by sources.

### Directory validation

The validator CLI checks the full directory, mapping and trade forms with all profiles,
independently of the profile paths supplied for schema validation. Errors fail the command:

- Directory ids are unique and well formed. Trade-form ids are unique, well formed and distinct
  from directory ids.
- Every id in `seriously-fish.yaml`, when the file exists, belongs to the directory.
- Every legacy record appears exactly once. Foreign legacy ids, unknown match kinds and
  missing non-open targets are errors. An open mapping has a null target; the total is a warning.
- A described profile whose valid name is absent from the directory produces a warning naming
  that profile. Every other profile has a trade-form entry, or validation reports an error.
- Every profile legacy id maps to that profile id or the directory entry for its valid name.
  Missing, open or contradictory mappings are errors.
- A correction has a non-empty reason in `note`.
- The `profile` of each directory entry names the described profile with the same valid name, or
  is `null` when none exists. A reference to a missing profile is an error; a stale reference is a
  warning, because parallel pipelines write profiles before the directory is rebuilt. Rebuilding the
  directory after adding a profile refreshes the references.

## Cichlid Room Companion

```
xvfb-run -a node tools/species-data/crc.mjs search "Tropheus moorii"
xvfb-run -a node tools/species-data/crc.mjs page "https://cichlidae.com/species.php?id=..."
```

`search` lists matching species, genus and article pages; `page` prints the readable text of one page and its subpage links. Every call starts its own browser, so parallel agents do not share state. All calls share a lock with a minimum interval of 15 seconds, retry twice after a rate limit (Cloudflare error 1015) and cache the printed text for 30 days in `data/species/work/.crc-cache/`.

## Internal source archive

```sh
node tools/species-data/archive-fetch.mjs https://www.seriouslyfish.com/species/neolamprologus-leleupi/
node tools/species-data/archive-fetch.mjs https://example.org/article --refresh
node tools/species-data/archive-backfill.mjs --dry-run
node tools/species-data/archive-backfill.mjs
node tools/species-data/fetch-oa.mjs --migrate-library
```

The archive root is `/workspaces/cichlids-source-archive`; `CICHLIDS_SOURCE_ARCHIVE` overrides it.
It lives outside the repository, stays internal and is never published. Tests use temporary roots.
Every cited web page is fetched through `archive-fetch.mjs`, and scientific full texts through
`fetch-oa.mjs`. Agents put the printed reference in the source entry's `archived` field. A source
may carry `offline: true` when its URL does not answer. The validator rejects malformed references
and warns about missing snapshot directories when the archive root exists.

A snapshot has this layout:

```text
<host>/<sanitized path segments>/<YYYY-MM-DDTHHMMSSZ>/
  original.html | original.pdf | original.xml | original.bin
  text.txt
  meta.json
index.jsonl
failures.jsonl
```

The host omits `www.`. Each path segment uses `[a-z0-9._-]` and at most 80 characters. The root
path uses `_root`. A query adds `q-<first 8 hex characters of the query's SHA-256>` after the path.
The timestamp is UTC. For example, a reference is
`seriouslyfish.com/species/neolamprologus-leleupi/2026-10-14T093000Z`. References are relative to the
archive root. Timestamp collisions advance the directory timestamp by one second; metadata retains
the actual fetch time. Exact URL matching keeps distinct URLs separate even when sanitized paths match.

The original body keeps its bytes. HTML extraction removes scripts, styles and tags and prefers
the article or main content; PDF extraction uses `pdfjs-dist`, as does `fetch-oa.mjs`. XML gets
readable text, including the structured TEI extraction for scientific documents. `meta.json`
records `url`, `finalUrl`, `fetchedAt`, `httpStatus`, `contentType`, `sha256`, `bytes`, `tool` and
`version`. `index.jsonl` has one line per snapshot with `ref`, `url`, `fetchedAt` and `httpStatus`.
Copies on `web.archive.org` follow the same rules and retain that archive URL in metadata.

Without `--refresh`, an existing snapshot of the exact URL is served with no network request.
The CLI prints the reference, the absolute path of `text.txt` and the first 1500 text characters.
Plain requests identify themselves as `cichlids.com-species-research/1.0`, have timeouts and follow
redirects. Per-host locks in the system temporary directory keep request starts at least two
seconds apart across processes, including redirect destinations and the plain full-text routes.
The headed Chromium full-text route retains its browser identity and session throttle.

Only publicly reachable content is eligible. No client impersonation, login, paywall or captcha
bypass is permitted. HTTP errors from status 400, network and certificate failures, browser error
pages, bot checks, login and paywall pages, and text shorter than 200 characters produce no
snapshot. `failures.jsonl` records `url`, `time` and `reason`; the fetch CLI exits non-zero.

Backfill reads `data/species/profiles/*.yaml`, `data/species/hybrid-groups.yaml` and
`data/species/waterbodies.yaml`. It archives each URL once and reuses snapshots. `--dry-run` only
lists unique URLs and changes no files. A successful source gets `archived: <ref>` after its URL
scalar, with matching indentation; quoted continuations stay intact. Flow mappings get the field
before their closing brace. Entries with `archived` stay untouched, and every other byte stays
unchanged. Failed sources retain their YAML and the command reports them with a non-zero exit.

## Open-access full texts

```
xvfb-run -a node tools/species-data/fetch-oa.mjs 10.3390/fishes11020106
xvfb-run -a node tools/species-data/fetch-oa.mjs https://doi.org/10.3390/fishes11020106
```

The tool gives research agents the text of open-access works. It prints an archive reference and a metadata summary (title, DOI, year, license, open-access status, route, source), the path of the text file and the first 1500 characters.

Library: `data/species/work/.literature/` (ignored by git). Per work it holds `<key>.json` (metadata), the original as `<key>.xml`, `<key>.pdf` or `<key>.html`, and the plain text `<key>.txt`; the key is the lowercase DOI with every character outside `[a-z0-9._-]` replaced by `_`. A work with a text file is served from the library without network access, so each work is downloaded once. Every full text also has an archive snapshot under `doi.org/<sanitized DOI>/<UTC timestamp>/`, with `archived` in its library JSON. The DOI uses the library key as one path segment, limited to 80 characters. `--migrate-library` copies library originals into the archive without network access, retains `fetchedAt` where metadata records it, writes archive references into the JSON files and prints those references. Repeated migration reuses snapshots. Error documents are rejected before any full text is stored or reported.

Open-access condition: OpenAlex (API key as for literature-search) must report `open_access.is_oa` as true. The routes are tried in this order, and the metadata field `route` names the one that delivered the text:

1. `openalex-tei`: the TEI XML copy hosted by OpenAlex (`has_content.grobid_xml`); the text holds title, abstract, section heads and paragraphs of the body.
2. `openalex-pdf`: the PDF copy hosted by OpenAlex (`has_content.pdf`), read with `pdfjs-dist`.
3. `publisher-fetch`: the open-access addresses of the work (best location first, then the other open-access locations; PDF address before landing page) with a plain request and the honest research user agent.
4. `publisher-browser`: the same address in a headed Chromium on a virtual display when the plain request is blocked (HTTP 403, 429 or 503, a bot challenge page, or an HTML page where a PDF is expected). Browser sessions of all processes share a lock and keep 10 seconds between them.

Each OpenAlex content download costs $0.01 of the free daily budget of the API key (about 100 downloads per day). On HTTP 429 or an "Insufficient budget" answer the tool prints a message and continues with the publisher routes. Without an API key the OpenAlex routes are skipped.

Pages that ask for a login, a subscription, a purchase or a captcha are not fetched: a marker such as "sign in", "purchase", "subscribe", "access denied" or "captcha" in the title or the start of a short page, or an address on a login host, ends the run.

Exit codes: 0 text available, 1 failure (unknown DOI, no route delivered usable text), 2 usage, 3 not open access (use the abstract from literature-search or another source), 4 blocked by login or paywall.

## Validate

```
node tools/species-data/validate.mjs                 # all of data/species/profiles/*.yaml
node tools/species-data/validate.mjs path/to/a.yaml  # given files
```

Every file is checked against `data/species/schema/species-profile.schema.json` (JSON Schema 2020-12 with the formats date and uri). Files in a directory named `profiles` are final profiles and get further checks:

- the file name equals `<id>.yaml`;
- every range has min <= max, and maxSizeCm for male and female is greater than 0 and at most 150;
- each hard feature (temperatureC, ph, ghDgh, predation, diet, speciesOnly, hybridGroups) has a verification object;
- each hard feature and tolerance with confidence `high` has at least one evidence entry whose source has trust `high` in `data/species/source-trust.yaml`; values supported only by medium or low trust remain valid at confidence `medium` or `low`;
- when `features.origin.value.waterbody` matches an entry of `data/species/waterbodies.yaml` (id, name or alias, case-insensitive), temperatureC, ph and ghDgh have verification method `waterbody` with that entry's id, and temperatureC, ph, ghDgh and khDkh equal the entry's ranges exactly. Any verification with method `waterbody` references an existing entry and its value equals that entry's range; the method applies to temperatureC, ph and ghDgh only. A missing table file skips the whole rule with a warning;
- every hard feature, including values verified by `waterbody`, has at least one `evidence` entry of any trust level;
- temperatureC, ph, ghDgh and khDkh have a `tolerance` (see Tolerance) whose value contains the natural range, whose verification method is unanimous, qualified-majority, median or adjudicated (not waterbody), with the evidence rule of the hard features (a median tolerance needs one evidence entry of any kind and confidence `low`; an adjudicated tolerance follows the adjudicated rule below); no other feature has a tolerance;
- predation, diet, speciesOnly and hybridGroups with verification method `conservative` have confidence `low`;
- the verification method `median` is accepted only on the natural values and tolerances of temperatureC, ph, ghDgh and khDkh (not on predation, diet, speciesOnly or hybridGroups); a median value or tolerance has confidence `low` and at least one evidence entry of any kind;
- the verification method `adjudicated` is accepted only on the natural values of temperatureC, ph, ghDgh and khDkh and on their tolerances; it replaces a tally decision that is implausible for aquarium keeping and requires `votes`, `replaced` (the tallied range) and `replacedMethod` (unanimous, qualified-majority, conservative or median). An adjudicated value or tolerance has confidence `low` or `medium` and at least one evidence entry of any kind and keeps the named-waterbody rule;
- id and legacyIds are unique across all validated profiles;
- every group of `data/species/hybrid-groups.yaml` has a unique `id`, a `description`, at least two `members` (name strings), at least one source, a `basis` of `documented` or `presumed` and a non-empty `userNote` (one or two plain-English sentences for hobbyists that explain the background of the group: what was observed, where and by whom where known, and how reliable the evidence is; no instructions); a violation is an error that names the group file;
- a group id in a hybridGroups value (a list of unique strings) that is not listed in `data/species/hybrid-groups.yaml` is a warning, because agents may propose groups the file does not list.
- a final profile and the group file name each other: a group whose `members` include the valid name of the profile and is missing from its hybridGroups value is a warning, and so is a group in the value whose `members` lack that name.

Other files, such as pipeline intermediates under `data/species/work/<id>/`, are checked against the schema only.

Source trust uses the first list entry whose host matches the URL host or a parent domain and whose optional path is a prefix of the URL path. Copies at `web.archive.org/web/<timestamp>/<url>` and `archive.org/web/<timestamp>/<url>` use the archived URL. Sources without a URL use `byKind`; other sources use the default, `low`. An unlisted host produces no issue. The trust list requires unique host and path pairs and levels of `high`, `medium` or `low`. Trust signals confidence and conflict weight; all sources stay cited and a low trust level does not invalidate a value.

The report lists file, JSON pointer and message per issue. The exit code is 1 when at least one error exists and 0 otherwise; warnings do not fail the run.

## Evidence

A fact may carry `evidence`: a list of `{source, value, note?}` entries. `source` has the shape of a profile source, `value` is the value that source states, converted to the unit and shape of the feature (such as `{min: 24, max: 26}` or a level), and `note` is a short qualifier in own words (such as "lake surface water" or "aquarium recommendation"). The species page lists which source states which value when a value is uncertain; it shows values and sources, never copied text. `tally.mjs` collects the entries of all votes and the review page shows them under "Angaben der Quellen".

## Tolerance

Each water feature (temperatureC, ph, ghDgh, khDkh) keeps the natural-habitat range of the species in `value` and carries a `tolerance`: the range in which the species stays healthy long-term according to literature and husbandry experience (care sheets count as sources). A tolerance has the fields of a fact (`value` as a range, `confidence`, `rationale`, `sources`, `evidence`, `verification`). With 5 or more votes and no qualified majority, its decision is the median range with confidence `low`. The intersection lets the single narrowest proposal set the limit and produces water conflicts absent from aquarium practice; the median range reflects what the sources recommend. Every decided tolerance is widened to contain the decided natural range. The app warns when the tolerances of two species do not overlap and shows a lesser level when only the natural ranges do not overlap.

## Tally

```
node tools/species-data/tally.mjs data/species/work/<dir>
```

The script reads `generator.yaml`, `checker-a.yaml`, `checker-b.yaml`, the optional `checker-c.yaml` (an independent checker of another model family) and every `tiebreaker-N.yaml` of the work directory and writes `tally.yaml` next to them. Tie-breaker files hold only `features` (a subset of the hard features) and optionally `provenance`; they are read without schema validation. Exit code 2 means a required input file is missing or the waterbody table is invalid; otherwise the exit code is 0.

Each file that contains a hard feature casts one vote with the feature's `value`. The vote order is generator, checker-a, checker-b, checker-c, tiebreaker-1, tiebreaker-2, and so on.

- Water body: the most common `features.origin.value.waterbody` over all files is looked up in the table. When it matches, temperatureC, ph, ghDgh and khDkh are the table's ranges with verification `{method: waterbody, waterbody: <id>}`, the table's sources and confidence high; they are not voted on.
- Voted features are predation, diet, speciesOnly and hybridGroups, plus temperatureC, ph, ghDgh and khDkh when no water body applies (khDkh is voted so that a decided tolerance always contains the natural carbonate hardness).
- Agreement: predation and speciesOnly by equality, hybridGroups by equality as sets (the order of the ids does not matter; the decision is a sorted list); a work file with the single-valued `hybridGroup` (an id or null) is read as `hybridGroups` with `[id]` or `[]`; diet by `type` and `bloatSensitive`; ranges when both bounds differ by at most 1 (temperatureC), 0.3 (ph) or 2 (ghDgh).
- The top candidate is the proposal with the most agreeing votes; on a tie the first file in vote order wins. The decided range is the median of the agreeing minima and the median of the agreeing maxima (one decimal for ph, integers otherwise).
- Source support consists of at least 2 distinct sources (by url, else title) of any trust among the agreeing votes, with at least one source of trust `medium` or `high` according to `data/species/source-trust.yaml`. Every source stays cited, including sources from dissenting votes; only agreeing votes contribute source support and confidence.
- With fewer than 5 votes the feature is `unanimous` when at least 3 votes exist, all of them agree and source support meets the trust rule. Otherwise it is open with `needsTiebreaker: true` and the reason `disagreement`, `too-few-votes`, `insufficient-sources` or `no-votes`; a tie-breaker agent adds one vote.
- With 5 or more votes the feature is `qualified-majority` when the top candidate has at least 4 agreeing votes and source support meets the trust rule. Without a qualified majority, predation, diet, speciesOnly and hybridGroups are `conservative` with confidence low: the maximum predation level; speciesOnly true if any vote says true; for hybridGroups the union of all groups named by at least two votes, or when no group reaches two votes the first group named by the earliest vote, and the empty list when no vote names a group; for diet the strictest type (aufwuchs, piscivore, invertivore, omnivore) and bloatSensitive true if any vote says so.
- A natural water range or tolerance (temperatureC, ph, ghDgh, khDkh) with 5 or more votes and no qualified majority is decided as `median` with confidence low: the median of the minima and the median of the maxima of all votes, rounded like the other ranges. The intersection of many votes is narrower than the measurements, so the median range describes the habitat better.

Each water feature also gets a tolerance entry named `<feature>.tolerance` (for example `ghDgh.tolerance`), voted from the files' `tolerance` values, also for lake species, because the water body decides the natural value only. The agreement thresholds are 1 (temperatureC), 0.3 (ph), 2 (ghDgh) and 2 (khDkh); the unanimous, tie-breaker and qualified-majority rules are the same. With 5 or more votes and no qualified majority, a tolerance is `median` with confidence low: the median of all minima and the median of all maxima, rounded to one decimal for ph and to integers for temperatureC, ghDgh and khDkh. Every decided tolerance, whatever its method, is widened to contain the decided natural range when that range reaches beyond it. Open tolerance entries appear in `pendingTiebreakers` by their entry name.

Per feature the output has `decision`, `verification`, a `confidence` suggestion (low for conservative and median; for unanimous and qualified-majority decisions, high with at least 3 distinct sources among the agreeing votes including at least one high-trust source, otherwise medium, capped by the median confidence the agreeing agents gave), `needsTiebreaker` and `reason` when open, `proposals` (file and value) and `sources`, and `evidence` (the evidence entries of all votes, once per source and JSON-equal value; empty for water body decisions). The top-level `pendingTiebreakers` lists the open features.

## Review page

```
node tools/species-data/render-review.mjs out.html [profile.yaml ...]
```

Writes a self-contained, read-only HTML page. Water cards show the tolerance below the natural value with its verification badge and its list of evidence. Hard feature cards show the verification method (einstimmig, qualifizierte Mehrheit, vorsichtigster Wert, Seewerte) and the votes as "4 von 5".

## Knowledge articles

```
node tools/species-data/validate-articles.mjs                  # all of data/knowledge/articles/*.md
node tools/species-data/validate-articles.mjs path/to/a.md     # given files
```

Articles explain topics beyond a single species, such as the water of Lake Malawi. Each one is a file `data/knowledge/articles/<id>.md`: YAML frontmatter plus a Markdown body. The frontmatter follows `data/knowledge/schema/article.schema.json` (`schemaVersion`, `id`, `title`, `language`, `summary`, `topics`, `species`, `waterbodies`, `sources`, `provenance`). Source and provenance entries have the shapes of the species schema, which the validator registers under its `$id`. `sources` maps a citation key (`^[a-z0-9-]+$`) to a source.

The validator reports:

- frontmatter errors against the schema, a summary of other than 1 to 3 sentences, and a file name that differs from `<id>.md`;
- citations: the body cites sources as footnote references `[^key]`; every key exists in `sources` and every source is cited at least once;
- every body paragraph and every list item has at least one citation (in an article of `kind: methodology`, which describes a procedure of cichlids.com, a missing citation is a warning); headings, tables, code blocks and lines holding only a list marker are exempt;
- `waterbodies` ids that are not in `data/species/waterbodies.yaml` are errors (the check is skipped with a warning when the table file is missing); `species` ids without a file in `data/species/profiles/` are warnings;
- archive sources follow the title and url rule of the profile validator;
- em or en dashes in the body are warnings.

The exit code is 1 when at least one error exists.

## Tests

```
cd tools/species-data
npm test
```

`npm test` runs every `*.test.mjs` file with the built-in Node test runner. The fixtures are small inline profiles, feature sets and waterbody tables.
