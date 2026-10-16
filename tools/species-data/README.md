# Species data tools

Tooling around the species catalog in `data/species/`.

## Contents

- `literature-search.mjs`: searches scientific literature through OpenAlex (with an API key from `OPENALEX_API_KEY` or `~/.config/cichlids/openalex.key`), Europe PMC and Crossref, throttled across processes, and prints year, title, DOI, open-access link and abstract, so research agents need no web search for literature. Each printed result is cached for 30 days in `data/species/work/.literature/search/<sha1 of query and limit>.txt`; a cached query prints the cached text without network access.
- `source-archive.mjs`: stores internal source snapshots and shares text extraction, access checks and process-safe host throttling with the research fetchers.
- `archive-fetch.mjs`: fetches and archives a public web page or document and prints its reference and readable text.
- `archive-search.mjs`: searches the cichlids.com forum archive and prints each matching thread with its citation (see Forum archive search). `archive-search.test.mjs` holds its tests.
- `archive-query.mjs`: the request validation, the fixed SQL query with its `docker exec` call, the formatting and the output cap that `archive-search.mjs` and the archive search service share. `archive-query.test.mjs` holds its tests.
- `archive-service.mjs`: the archive search service on a unix socket, for caged agents. `archive-service.test.mjs` holds its tests.
- `archive-backfill.mjs`: archives unique source URLs from profiles and shared tables and inserts missing archive references without reformatting YAML.
- `fetch-oa.mjs`: fetches the full text of open-access works by DOI into a local library (see Open-access full texts).
- `crc.mjs`: reads the public part of the Cichlid Room Companion for the agents and keeps each page read in the source archive. The site blocks curl and headless browsers through a Cloudflare bot check, so the tool drives a headed Chromium on a virtual display.
- `export-legacy-catalog.mjs`: exports the legacy species catalog with photo counts to `data/species/legacy-catalog.json`. It reads the restored legacy MySQL database read-only through its running container and holds catalog data only.
- `prompts/`: prompts of the agent pipeline (generator, checker, tie-breaker, adjudicator and the shared rules) that produce species profiles.
- `agent-runner.mjs`: starts one agent role with Codex (`codex exec`) or Claude (`claude -p` in the cage), picks the model per runner and role, retries transient failures with fallback models and recognises an empty account. `agent-runner.test.mjs` holds its tests.
- `agent-cage.mjs`: the bubblewrap cage of the Claude agents (arguments, environment allowlist, write root, read directory and work directory validation). `agent-cage.test.mjs` holds its tests; `agent-test-support.mjs` holds their fixtures.
- `cage-init.mjs`: the init program inside the cage; it relays two loopback ports to the proxy sockets and starts the agent. `cage-init.test.mjs` holds its tests.
- `credential-proxy.mjs`: the proxy on a unix socket that adds the real credential to the requests of a caged agent. `credential-proxy.test.mjs` holds its tests.
- `egress-proxy.mjs`: the proxy on a unix socket that gives a caged agent access to public hosts on the ports 80 and 443. `egress-proxy.test.mjs` holds its tests.
- `proxy-process.mjs`: the child process of the runner that hosts both proxies and the archive search service, with the start function of the runner and the exit notice. `proxy-process.test.mjs` holds its tests.
- `relay-response.mjs`: the checked forwarding of an upstream answer in both proxies. `relay-response.test.mjs` holds its tests.
- `browser-proxy.mjs`: the proxy option of the Chromium launches of `fetch-oa.mjs` and `crc.mjs`. `browser-proxy.test.mjs` holds its tests.
- `agent-audit.mjs`: the check of the write roots after a caged attempt. `agent-audit.test.mjs` holds its tests.
- `safe-write.mjs`: the writes of the tools into directories that an agent can write (symlink checks below a trusted base). `safe-write.test.mjs` holds its tests.
- `claude-agent.mjs`: runs one Claude agent for a one-off job (research, review, coding) with the runner machinery of the pipeline (see Agent CLI). `claude-agent.test.mjs` holds its tests.
- `pipeline-prompts.mjs`: builds the prompts of the pipeline roles and of the group sync step. `pipeline-prompts.test.mjs` holds its tests.
- `pipeline.mjs`: the pipeline driver and its command line (`run`, `groupsync`, `next`, `status`). `pipeline.test.mjs` holds its tests.
- `yaml-unalias.mjs`: expands YAML anchors and aliases (on list items and mapping values) in the given files while keeping the text layout, and rewrites a file only when the expanded text parses to the same value. `yaml-unalias.test.mjs` holds its tests.
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

## Forum archive search

```sh
node tools/species-data/archive-search.mjs "Altolamprologus" 10
```

`archive-search.mjs "<term>" [limit]` searches the posts of the cichlids.com forum archive (the
legacy threads in the local dev database) and prints each matching thread with its legacy thread
id, its legacy forum URL and excerpts of the posts (600 characters each). It prints post text only,
never member names. The term is a string of 2 to 100 characters without control characters and
matches case-insensitively inside the post text (`%` and `_` act as in `ILIKE`); the limit is an
integer from 1 to 100 and defaults to 30. The query is fixed: the term reaches `psql` as a
variable (`-v term=...`), never as part of the SQL text. A search with no match prints
`No posts found.`; a refused request or a failed search prints one `error: <message>` line to
stderr and exits with 1 (2 for a refused command line).

Two modes share the validation, the query and the formatting (`archive-query.mjs`):

- Without `CICHLIDS_ARCHIVE_SEARCH_SOCKET` (operator use outside the cage) the command runs
  `docker exec -i stack-postgres-1 psql ...` itself, with a timeout of 30 s.
- With `CICHLIDS_ARCHIVE_SEARCH_SOCKET` set (the cage environment sets it to
  `/run/cage/archive.sock`) the command sends the request to the archive search service behind that
  unix socket and prints the answer. The runner starts the service with the proxies, outside the
  cage, for the duration of one call (see Agent cage).

Protocol of the service: one request per connection. The client sends one JSON line,
`{"term": "...", "limit": 30}` (`limit` is optional), and the service answers the exact text that
the command prints, or one line `error: <message>`, and closes the connection. A request with a
field other than `term` and `limit`, an invalid term or limit, a line of more than 4096 bytes or
no line within 10 s gets an error line. Limits: 4 queries at once (the next request answers
`error: too many archive queries at once, retry shortly`), 30 s per query (the database cancels the
statement too), 200 KB per answer (cut at a line with the closing line `[output cut at 200 KB]`).
The service never logs a term or a result.

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
- a natural water range (temperatureC, ph, ghDgh, khDkh) whose min equals its max is a warning, because a single reading is a point and the planner treats the natural range as the ideal;
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

## Pipeline

```
node tools/species-data/pipeline.mjs run --runner codex --billing subscription --id 201 --name "Xenotilapia flavipinnis" --hint "Lake Tanganyika sand cichlid" --lake Tanganyika
node tools/species-data/pipeline.mjs run --runner claude --billing api-key --id 405 --name "Nimbochromis linni" --hint "Malawi peacock" --lake Malawi --legacy-ids "405, 289" --adjudicator-note "Cyrtocara linni is a synonym."
node tools/species-data/pipeline.mjs next --count 4
node tools/species-data/pipeline.mjs status
node tools/species-data/pipeline.mjs groupsync --runner claude --billing api-key
```

One `run` produces the profile of one legacy catalog entry:

1. The roles generator, checker A, checker B and checker C research the species in parallel and each write a file (`generator.yaml`, `checker-a.yaml`, ...) to the work directory `data/species/work/legacy-<id>/`.
2. `validate.mjs` checks each of the four files.
3. `tally.mjs` counts the votes. Open features go to two tie-breakers (`tiebreaker-N.yaml`, numbered on from the highest existing file). The tally runs at most three rounds; features open after the third round end the run as failed.
4. The adjudicator writes the final profile to `data/species/profiles/`. Its prompt forbids changing an existing profile of the same species: in that case it writes `adjudicator-profile.yaml` to the work directory and starts its report with `PROFILE EXISTS`, and the run logs `WARN: profile exists, see adjudicator-profile.yaml`. The run fails with `FAILED: adjudicator failed` when every attempt of the adjudicator failed transiently, and with `FAILED: adjudicator wrote no profile` when `logs/adjudicator.last` is empty or when neither a profile in `data/species/profiles/` whose top-level `legacyIds` contain `--id` nor `adjudicator-profile.yaml` was created or changed during the adjudicator step (profiles of other species that parallel runs write do not count). A passed check is recorded in `adjudicator.done` in the work directory.
5. `build-directory.mjs` rebuilds the directory and `validate.mjs` checks all profiles.

`--id` is the legacy catalog number (digits only). `--lake` selects the lake model row for species of a lake in `waterbodies.yaml`. `--legacy-ids` lists further legacy entries that map to the species and defaults to `--id`. `--adjudicator-note` is added to the adjudicator prompt.

`next` lists the unprofiled species with the most legacy photos as `<profile id> | <category> | <legacy entries>`. It skips trade forms with parents and species with a run in progress, that is a work directory whose `run.lock` names a live process. `status` prints the last status line of every work directory as `<id> <name> | <line>`, followed by `(running)` when the lock names a live process and by `(stale)` when the lock names a dead process or when the last line is neither `DONE` nor `FAILED: ...` and no lock exists. When the role logs of the directory hold cost lines, the row ends with ` | cost <x.xxxx> USD` (the sum of all `=== cost` lines), and with ` | billing <api-key|subscription>` for the latest Claude attempt.

`groupsync` collects the validator warnings about profiles that a group of `hybrid-groups.yaml` names as a member while the profile does not list that group. The agent edits exactly those profiles (group ids, rationale and sources in `features.hybridGroups`, and a hybrid caution where a profile names a group member as a suitable partner) and does not touch `hybrid-groups.yaml`, because other runs write that file at the same time. It prints `nothing to sync` when no warning exists, otherwise the last message of the agent and `git diff --stat`. Its prompt, log and last message are in `data/species/work/groupsync/logs/`. Exit codes of `groupsync`: 0 synced or nothing to sync, 1 the agent failed or left no last message, 2 usage error, 3 the agent account is out of credits.

Models per runner (`MODEL_TABLE` in `agent-runner.mjs`). Checker A always runs with Claude, so two model families vote in a Codex run. Each role tries its model three times, then the fallbacks once each.

| Role | Codex | Claude |
| --- | --- | --- |
| generator, checker B (lake species) | gpt-6-luna, high | claude-sonnet-5-5, high |
| generator, checker B (other species) | gpt-6.1-sol, medium | claude-sonnet-5-5, high |
| checker C (lake species) | gpt-6-luna, high | claude-opus-5-5, high |
| checker C (other species) | gpt-6.1-sol, medium | claude-opus-5-5, high |
| checker A | claude-sonnet-5-5, high | claude-sonnet-5-5, high |
| tie-breakers | gpt-6.1-sol, medium | claude-sonnet-5-5, high |
| adjudicator | gpt-6.1-sol, high | claude-opus-5-5, high |
| group sync | gpt-6-luna, high | claude-sonnet-5-5, high |

Codex falls back to gpt-6-sol twice.

Logs live in `data/species/work/legacy-<id>/logs/`: per role `<role>.prompt`, `<role>.log` (every attempt under a line `=== attempt <n> <model> <ISO time>`, followed by ` billing <api-key|subscription>` for Claude attempts, closed by a line `=== exit <code>` with the signal and the timeout when they apply and, for Claude attempts that report a cost, a line `=== cost <usd> USD`) and `<role>.last` (the last message of the latest attempt), the output of the validator, tally and directory build as `<name>.txt`, and `status` with one line `HH:MM <text>` per step (`start`, `previous role files moved to previous-<stamp>`, `stale lock of process <pid> replaced`, `adjudicator note changed`, `<role> finished (<model>)`, `<role> failed (<model>)` when every attempt failed transiently, `adjudicator skipped (adjudicator.done)`, `DONE`, `FAILED: ...`, `WARN: ...`). `DONE` or a `FAILED: ...` line is always the last line of a run: a role that runs in parallel to a failure appends nothing after it. A thrown error ends the run with `FAILED: crashed (<message>)`; SIGINT and SIGTERM end it with `FAILED: crashed (<signal>)` after all running agents have ended, with exit code 1. The work directory also holds `run.json` with the inputs of the run.

A run holds the lock `run.lock` (the pid of the run) in its work directory from the start until it ends. The lock appears with its content in one step (a hard link of `run.lock.<pid>`), and a stale lock is renamed and checked before it is removed, so two runs that start together never both hold it. A second `run` of the same species while that process lives prints an error and exits with 1; a lock of a dead process (for example after SIGKILL) is replaced. Each agent attempt runs at most one hour (40 minutes for group sync); a timed-out agent gets SIGTERM and, when it has not ended 10 s later, SIGKILL.

A run resumes where it stopped: roles whose `<role>.yaml` exists are skipped, and tie-breakers continue after the highest existing number. The adjudicator is skipped when all of these hold: `adjudicator.done` exists and the files it names exist, `logs/adjudicator.last` is not empty, and the inputs of the adjudicator are unchanged. A changed `--adjudicator-note` removes `adjudicator.done` before `run.json` stores the note, and every voting role or tie-breaker removes it before the agent starts, so a run that stops after such a change reruns the adjudicator on resume. A rerun with different inputs, or with `--fresh`, moves all entries of the work directory except `.literature` and the lock files `run.lock*` into `previous-<YYYYMMDD-HHMMSS>/` and starts over. The inputs that decide this are `--id`, `--name`, `--hint`, `--lake` and `--legacy-ids`. A rerun that differs only in `--adjudicator-note` keeps the research files, stores the note in `run.json`, logs `adjudicator note changed` when an earlier adjudicator record exists, and runs the adjudicator. The profile of the earlier run exists at that point, so the adjudicator prompt makes the adjudicator leave it untouched and write its candidate to `adjudicator-profile.yaml`; the run logs the `WARN: profile exists, see adjudicator-profile.yaml` line. The operator reviews the candidate and moves it over the profile; the pipeline never moves a profile itself. A work directory with role files and no `run.json` is moved the same way.

Exit codes of `run`: 0 done, 1 failed (the status file names the step), another process runs the species, an agent's credential is rejected or an agent audit fails, 2 usage error, 3 an agent account is out of credits. Run at most four species in parallel.

### API key file

The billing of every `claude -p` child that the tools start (pipeline roles, group sync, checker A of a Codex run, the agent CLI) is an explicit choice per command: the required option `--billing api-key|subscription` of `pipeline.mjs run`, `pipeline.mjs groupsync` and `claude-agent.mjs`. It has no default, and the presence of the key file never decides it. A missing or invalid value ends the command with exit code 2 before a lock, a manifest, a status line or an agent exists. `pipeline.mjs next` and `status` take no billing option.

- `api-key` bills the Anthropic API key in the key file. Before the first agent starts, the tools check that the file exists, is a regular file, is readable and holds non-whitespace content; otherwise the command ends with exit code 2 and a message naming the path and the reason. There is no fallback to the subscription.
- Each role verifies the key file before it starts. A check that fails ends a `run` as `FAILED: crashed (...)` and exit code 1, and `groupsync` and the agent CLI print one error line and exit with 1.
- `subscription` bills the claude.ai login of the user. Before the first agent starts, the tools check that `~/.claude/.credentials.json` is readable and holds a `claudeAiOauth` access token whose `expiresAt` (epoch milliseconds) lies in the future; otherwise the command ends with exit code 2. The login needs a fresh claude.ai login on the host, and a running Claude Code login on the host keeps the file current. The tools never refresh the token: a token that expires during a run ends the attempt as an authentication failure.
- The default path of the key file is `~/.config/cichlids/anthropic.key`; `CICHLIDS_ANTHROPIC_KEY_FILE` overrides it. The file holds only the key, has mode 600 and is never committed.
- Neither credential reaches the agent. The credential proxy adds it to the requests (see Agent cage), and the environment of the agent holds a placeholder. So the chosen billing alone decides. Settings files cannot change it: every Claude child starts with `--setting-sources ""`, so the user, project and local settings files (`~/.claude/settings.json`, `<cwd>/.claude/settings.json`, `<cwd>/.claude/settings.local.json`) are not read, and with them the hooks and plugins of the user do not load in agents, and the child gets no `--settings` argument. Managed settings cannot be skipped, so the commands read `/etc/claude-code/managed-settings.json` before anything starts (a missing file is fine) and end with exit code 2 when it cannot be read, is not a JSON object, or sets an `apiKeyHelper` or an `env` entry that selects credentials or the endpoint (`ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_BASE_URL`, `CLAUDE_CODE_USE_BEDROCK`, `CLAUDE_CODE_USE_VERTEX`, `CLAUDE_CODE_USE_FOUNDRY`, and `CLAUDE_CODE_OAUTH_TOKEN` under `api-key`).
- Claude runs with `--output-format json`. The `result` field of the output is the content of `<role>.last`; stdout that is not JSON is kept in the log and leaves `<role>.last` empty. A run that reports `is_error` counts as unfinished whatever its exit code: `<role>.last` stays empty and the log holds its text under a line `=== result`.
- The billing of every attempt is in its `=== attempt` header, its cost in the `=== cost` line, and `pipeline.mjs status` shows both per run.
- A rejected credential (HTTP 401 or 403, "Invalid API key", "invalid x-api-key", `authentication_error`) ends the role at once with the result `auth-failed`, because a retry cannot fix it. The proxy answers 401 when the key file vanished after the check or the subscription token has expired. The run ends with `FAILED: <role> authentication failed (<hint>)` and exit code 1; the hint is `check the API key file` under API billing and `check the claude login` under subscription billing.

### Agent cage

Every Claude child (pipeline roles, checker A of a Codex run, group sync, the agent CLI) runs as `bwrap ... node cage-init.mjs ... -- claude -p ...` in a bubblewrap cage (`agent-cage.mjs`) without a network of its own, behind a credential proxy (`credential-proxy.mjs`), an egress proxy (`egress-proxy.mjs`) and an archive search service (`archive-service.mjs`), and is audited after each attempt (`agent-audit.mjs`). The cage holds under `bypassPermissions` and does not depend on any setting of the CLI. It is required: when `bwrap` is missing or fails to start, or when `cage-init.mjs` cannot open its relays or start `claude` (stderr line `bwrap: ...` or `cage-init: ...` with a non-zero exit code), the role ends as `failed` after one attempt, and nothing runs outside the cage. Codex children run with their own sandbox (`codex exec -s workspace-write`), without cage, proxies and audit; their environment lacks the session variables of a parent Claude Code session, every variable whose name marks a credential (`TOKEN`, `SECRET`, `PASSWORD`, `PASSWD`, `CREDENTIAL`, `PRIVATE`, `API_KEY`, `APIKEY`) and the variables that lead to credential agents, editors and the display (`SSH_AUTH_SOCK`, `GIT_ASKPASS`, `SSH_ASKPASS`, `VSCODE_*`, `GPG_AGENT_INFO`, `DBUS_SESSION_BUS_ADDRESS`, `XAUTHORITY`, `DISPLAY`), because Codex reads its login from `~/.codex/auth.json` and needs none of the others.

Network. The cage runs with `--unshare-net`: its network namespace holds a loopback interface and nothing else, so the loopback services of the container (databases, admin tools, the task board API, identity and object stores) and the network of the host are out of reach. Per call of the runner (all attempts share it) a directory with mode 700 is created in the temporary directory of the host, outside the cage, and bound read-only at `/run/cage` (connecting to a socket needs no write access). It holds three unix sockets: `api.sock` of the credential proxy, `egress.sock` of the egress proxy and `archive.sock` of the archive search service. Inside the cage `cage-init.mjs` runs with the host `node` binary, opens TCP listeners on `127.0.0.1:18080` (to `api.sock`) and `127.0.0.1:18081` (to `egress.sock`), starts `claude` with the inherited standard streams, passes SIGTERM and SIGINT on, and exits with the exit code of the child (128 plus the signal number when a signal ended it). `archive.sock` needs no relay: `archive-search.mjs` connects to it directly. The proxies and the archive search service run in one child process of the runner (`proxy-process.mjs`), so that a crash of one of them cannot stop the runner before the audit. A proxy process that ends while an attempt runs ends the child of that attempt; the audit runs after the end of the child as it does after every attempt, and without a hit the role ends as `failed` with the detail `the proxy process ended (<exit code or signal>)` and no further attempt. The proxy process has an empty environment and no output streams; the archive search runs `docker` with the docker variables of the runner environment (`PATH`, `HOME`, `DOCKER_HOST`, `DOCKER_CONTEXT`, `DOCKER_CONFIG`, `DOCKER_TLS_VERIFY`, `DOCKER_CERT_PATH`, `XDG_CONFIG_HOME`) and nothing else. The proxies, the service and the directory end with the call, also after an error.

What the cage shows:

- The whole file system read-only, a fresh `/dev`, a `/proc` with only the cage processes, and empty `/tmp` (at most 2 GiB), `/run` (no docker socket, at most 1 MiB) and home directory (at most 512 MiB); every tmpfs of the cage has a size limit when `bwrap` supports `--size`. The processes live in their own PID, IPC and network namespaces and a separate terminal session and end with the parent. When `bwrap --help` lists `--disable-userns` (checked once per process), the cage also blocks further user namespaces inside. `/sys` is visible as the host shows it to the container.
- An empty `/workspaces`: no other checkout, no legacy backups (at most 1 MiB). An empty `/X11-unix` (the display of the host accepts unauthenticated connections) and an empty `/var/lib/docker` (the file systems of other containers), each when it exists and each at most 1 MiB. The home directory holds no key file, no claude.ai or codex login and no dotfiles.
- Read-only on top of that: the repository root, the directory of the real `claude` binary (`claude` on the `PATH`, resolved with `realpath`), `~/.cache/ms-playwright`, the OpenAlex key `~/.config/cichlids/openalex.key` (each when it exists; `fetch-oa.mjs` and `crc.mjs` run inside the agent's Bash) and every `--read` directory of `claude-agent.mjs`. So an agent cannot change repository code, git hooks or `.claude`. The git directory of a worktree is not visible, so git commands fail inside the cage. The host `node` binary is bound too when it lies in the home directory, `/tmp`, `/run` or `/workspaces`.
- Read-write, the write roots: `<repo>/data`, the real path of `data/species/work`, the source archive root (`CICHLIDS_SOURCE_ARCHIVE` or its default) when it exists, and every `--write` directory of `claude-agent.mjs`. All binds use real paths. A write root is refused when it is or contains the repository root, the home directory or a git directory, lies inside a git directory or inside a `.git` or `.claude` directory (below the repository root for paths in the repository), is `/tmp` or `/tmp/claude-<uid>` itself, or (a `--write` directory) lies outside the repository and `/tmp`. `CICHLIDS_SOURCE_ARCHIVE` inside the home directory or the repository is an error. A `--write` directory can hold code (for example `tools/`): what an agent changes there runs later outside the cage, so someone must read and approve those changes before anything uses them.
- Read directories (`--read`) follow the rules of `--write` directories, except that the cage binds them read-only and that they may lie below the scratch directories of `/tmp/claude-<uid>`; the directory `/tmp/claude-<uid>` itself, the home directory, git directories and `.claude` directories are refused, and so is a read directory inside a write root. Inputs of a caged agent (briefs, notes) go into a `--read` directory and outputs that are files into a `--write` directory; the cage hides everything else under `/tmp`.
- The work directory `data/species/work` is a real directory of the checkout or a symlink into the main checkout (the parent of the git common directory). Every other target, for example one that an agent set, fails the call before anything starts. The check runs in every call.
- The working directory of the agent is the repository root (`--cwd` of `claude-agent.mjs` may name any directory inside the repository, a write root or a read directory).

The environment of the cage is an allowlist: `PATH`, `LANG`, `LC_ALL`, `LC_CTYPE`, `TERM` and `TZ` when set, `HOME`, `CLAUDE_CONFIG_DIR` (`<home>/.claude`, inside the empty home), `CICHLIDS_SOURCE_ARCHIVE` (its real path) when set, `ANTHROPIC_BASE_URL=http://127.0.0.1:18080`, `CICHLIDS_ARCHIVE_SEARCH_SOCKET=/run/cage/archive.sock` (always set to this value, whatever the parent environment holds), `HTTPS_PROXY`, `HTTP_PROXY` and their lowercase forms (`http://127.0.0.1:18081`), `NO_PROXY=127.0.0.1,localhost` (and lowercase), `NODE_USE_ENV_PROXY=1` (node `fetch` follows the proxy variables, curl does too) and one placeholder credential: `ANTHROPIC_API_KEY` under `api-key`, `CLAUDE_CODE_OAUTH_TOKEN` under `subscription`. Nothing else passes, so no variable of a parent Claude Code process and no secret of the environment reaches the agent. Chromium ignores the proxy variables; `fetch-oa.mjs` and `crc.mjs` pass the address of `HTTPS_PROXY` (or `https_proxy`) as the proxy server of `chromium.launch` (`browser-proxy.mjs`), and outside the cage, where the variable is not set, Chromium connects directly.

The credential proxy listens on its unix socket and closes at the end of the call. It serves the methods GET, POST and HEAD and only the paths `/v1/messages`, `/v1/messages/count_tokens`, `/v1/models` and below `/v1/models/` (with any query), and only when the target holds no `//`, no `://`, no backslash and no `..` segment (also after percent decoding); everything else answers 400 and the connection closes, so the Files and batch endpoints of the same workspace are out of reach. An upstream answer with a status outside 200..599, or one that node cannot forward, answers 502. The upstream host is fixed (`https://api.anthropic.com`). Per request it reads the credential fresh: under `api-key` it sets `x-api-key` to the content of the key file and drops `authorization`; under `subscription` it sets `authorization: Bearer <accessToken>` from `~/.claude/.credentials.json` and drops `x-api-key`. It answers 401 with an `authentication_error` body in the format of the API when the credential is missing, unreadable or (subscription) expired (`subscription login expired`). It forwards method, path, the other headers and the bodies, streaming, without hop-by-hop headers, and never logs a header or a body. Credits that an agent spends through the proxy are limited by the balance of the account.

The egress proxy handles `CONNECT host:port` and plain HTTP requests in absolute form, and nothing else. It allows the ports 80 and 443 only. It resolves the host itself (all addresses) and refuses when any address is a loopback, unspecified, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, fe80::/10), carrier-grade NAT (100.64/10), unique local (fc00::/7), multicast, broadcast, documentation or other reserved address, or an IPv6 form that holds such an IPv4 address (IPv4-mapped, NAT64, 6to4, IPv4-translated `::ffff:0:0:0/96`), or an SRv6 address (`5f00::/16`). It connects to one of the vetted addresses without a second lookup, so DNS rebinding does not lead elsewhere; a connection attempt waits at most 10 s per address, a tunnel or an upstream connection that stays silent for 5 minutes is closed, and the proxy takes at most 128 client connections at once. A refusal answers 403 with a short reason; an upstream answer with a status outside 200..599 (also a protocol switch), or one that node cannot forward, answers 502. The proxy does not look into tunnels and never logs a request. It lets an agent reach every public host on the ports 80 and 443, so it protects the network of the container and the host, not the data that the agent can read (the repository and the read and write roots). The public addresses of the docker host itself are not refused: a service that the host publishes on port 80 or 443 is reachable.

The archive search service listens on `archive.sock` and answers the forum archive search of `archive-search.mjs`, which the cage cannot run itself (no docker access, no network). It runs outside the cage with the access of the operator, but offers nothing beyond the one fixed query (see Forum archive search): the agent sends a term and a limit and gets post text back, no SQL, no member names. The request is validated before any query (a term of 2 to 100 characters without control characters, an integer limit of 1 to 100). At most 4 queries run at once and a further request answers an error line at once; a query runs at most 30 s; an answer is cut at 200 KB. When the container `stack-postgres-1` is not running or docker is not reachable, the service answers `error: the archive database is not available (...)`, which `archive-search.mjs` prints to stderr with exit code 1, so the agent sees why no archive thread is cited. The service never logs a term or a result, and an error that carries database or process text answers a generic message without it.

The audit scans the write roots inside the repository, the work directory and the source archive root after each Claude attempt, right after the child ends and before the last message and the log entry are written; it also runs after an abort or an error of the run, and its failure wins over every other outcome (also over a log write that the agent made impossible, for example with a symlink at `<role>.log`: the refused write is one more hit, and the hits go to the status file, or to stderr when the status file refuses the line). It enters no watched directory and follows no symlink. It fails the role when it finds
- a symlink whose real target lies outside all write roots and that did not exist with that target before the attempt (a symlink out of the roots that existed unchanged before is only named in the log as `=== audit warning <path> (symlink to <target>)`, once per call),
- an entry named `.claude`, `.git`, `.gitattributes`, `.codex`, `.agents`, `.envrc`, `.mcp.json`, `CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md` or `AGENTS.override.md` that did not exist before the attempt,
- a directory or symlink that cannot be read (any error other than a missing entry), or a tree of more than 200000 entries in all scan roots (the walk stops there; a tree beyond the limit fails every attempt).

A symlink that existed unchanged before the attempt does not fail the role, but it stays in the log as a warning, and a warning deserves a look: an agent of an earlier call may have planted it. `node_modules` directories are scanned for symlinks and not for watched names. A snapshot of the watched names and of all symlinks with their targets is taken before each attempt. The result is `audit-failed`; a failed audit leaves the last message of the attempt unwritten, the log holds the full list under `=== audit failed`, and the pipeline ends with `FAILED: <role> audit failed (<first paths>)` and exit code 1. `groupsync` and the agent CLI print the paths and exit with 1.

Files that the tools write into a directory an agent can write go through the helpers of `safe-write.mjs` (`safeWriteFile`, `safeAppendFile`, `safeMkdir`). Each call names a trusted base, normally the real path of the write root that holds the target, checks every path component below it with `lstat` and refuses a symlink anywhere on the way, also at the target. A full write goes through a temp file in the same directory plus a rename, and an appended file opens without following a symlink. The runner and the pipeline build every path under `data/species/work` from the validated real path of the work directory (the same check as for the cage) and use it as the base; the agent CLI writes its log and last message to an `--out-dir` that no agent can write. A run that meets a symlink ends with `FAILED: crashed (refusing to write through the symlink ...)` or, before the lock, with `cannot start`. These writers use the helpers: the pipeline and the runner (prompts, logs, `.last`, status, `run.json`, `adjudicator.done`, validator output, work and log directories), `pipeline-lock.mjs`, `tally.mjs`, `build-directory.mjs` (`directory.yaml`, `legacy-mapping.yaml`, `trade-forms.yaml`), `source-archive.mjs` (snapshots, index, failure log), `fetch-oa.mjs` (library), `crc.mjs` and `literature-search.mjs` (caches), `archive-backfill.mjs`, `check-seriously-fish.mjs`, `export-legacy-catalog.mjs`, `yaml-unalias.mjs` and `render-review.mjs` (the output file; there the directory of the file is the base, so only the file itself is checked). The fetchers (`fetch-oa.mjs`, `crc.mjs`, `literature-search.mjs`) use the plain path `<repo>/data/species/work` as the base and run inside the cage, where that path leads to the validated work directory; a manual run outside the cage trusts the path in the checkout, which in a worktree is a symlink that an agent can retarget, so check the link first. `pipeline.mjs` creates a missing `data/species/work` below `data` with `safeMkdir` and validates the result. The lock and timestamp files of the fetchers in the temporary directory of the host stay as they are: the cage has its own `/tmp`, so no agent can write there. Limits: a check happens before the write, so a component that another process swaps in between is not caught (the parallel roles of one run share the work directory); reads (for example of the prompt file or of agent output) follow symlinks.

Not covered by the cage: CPU, memory and process counts of the agent, `/sys`, a weakness of the kernel or of `bwrap`, and the content of the write roots (the audit checks symlinks and watched names, not the content of files).

The rules for the agents (sources, fetching, copyright, no state-changing git commands) are in the prompts of `prompts/` and `pipeline-prompts.mjs`. Claude agents get the tools Bash, Read, Write, Edit, Glob, Grep and WebSearch and no MCP tools (`--strict-mcp-config`), because agents fetch pages only through `archive-fetch.mjs` and use no external connectors.

## Agent CLI

```
node tools/species-data/claude-agent.mjs --name review-1 --prompt-file /tmp/review.txt --out-dir /tmp/agents --billing api-key --model claude-opus-5-5 --effort high --cwd . --timeout-min 30
```

Runs one Claude agent with the prompt from `--prompt-file` and the same setup as a pipeline role: bypassed permissions, the tools Bash, Read, Write, Edit, Glob, Grep and WebSearch, no MCP tools, no session persistence, the billing of `--billing`, retries of transient failures, and detection of an empty account and of a rejected credential. `--name` (`[a-z0-9][a-z0-9-]*`), `--prompt-file`, `--out-dir` and `--billing api-key|subscription` (checked as described under API key file) are required; `--model` defaults to `claude-sonnet-5-5`, `--effort` to `high`, `--cwd` to the current directory and `--timeout-min` to 60 (at most 35791). `--model` starts with `claude-` and `--effort` is one of `low`, `medium`, `high`, `xhigh`, `max`. An `--out-dir` that cannot be created is a usage error.

`--write <dir>` (repeatable) adds a read-write directory to the cage and `--read <dir>` (repeatable) binds a directory read-only into the cage at its real path (see Agent cage). Inputs of the agent (briefs, notes) go into a `--read` directory, outputs that are files into a `--write` directory; the cage hides everything else under `/tmp`. A relative path resolves against the repository root. A usage error (exit code 2) follows when the directory does not exist or is not allowed (the repository root or a parent, the home directory, a git directory, a place inside `.git` or `.claude`, `/tmp` or `/tmp/claude-<uid>` itself, a place outside the repository and `/tmp`, or a `--read` directory inside a write root). A `--read` directory may lie below the scratch directories of `/tmp/claude-<uid>`. A `--write` directory can hold code such as `tools/`; its changes must be read and approved before they run outside the cage. `--out-dir` is written by the CLI outside the cage (log and last message) and is not bound into it. Its real path must not lie inside a write root of the agent, inside the repository or inside the home directory (usage error otherwise), so that the agent cannot plant symlinks there. The agent writes only to the `--write` directories and the default write roots; it hands its result over in its final message.

The agent writes `<out-dir>/<name>.log` (format as in the pipeline logs) and `<out-dir>/<name>.last` (its final message) and the CLI prints one line: `<name>: <status> billing <billing> cost <x.xxxx> USD attempts <n> -> <out-dir>/<name>.last`. The status is `ok`, `failed` (also for an empty final message), `auth-failed`, `audit-failed` (the paths follow on stderr), `out-of-credits` or `aborted`. Exit codes: 0 ok, 1 failed, auth-failed, audit-failed or aborted (SIGINT and SIGTERM end the agent and exit with 1), 2 usage error, 3 the account is out of credits.

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
