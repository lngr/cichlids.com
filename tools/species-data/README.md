# Species data tools

Tooling around the species catalog in `data/species/`.

## Contents

- `literature-search.mjs`: searches scientific literature through OpenAlex (with an API key from `OPENALEX_API_KEY` or `~/.config/cichlids/openalex.key`), Europe PMC and Crossref, throttled across processes, and prints year, title, DOI, open-access link and abstract, so research agents need no web search for literature. Each printed result is cached for 30 days in `data/species/work/.literature/search/<sha1 of query and limit>.txt`; a cached query prints the cached text without network access.
- `fetch-oa.mjs`: fetches the full text of open-access works by DOI into a local library (see Open-access full texts).
- `crc.mjs`: reads the public part of the Cichlid Room Companion for the agents, which use it for verification only. The site blocks curl and headless browsers through a Cloudflare bot check, so the tool drives a headed Chromium on a virtual display.
- `export-legacy-catalog.mjs`: exports the legacy species catalog with photo counts to `data/species/legacy-catalog.json`. It reads the restored legacy MySQL database read-only through its running container and holds catalog data only.
- `prompts/`: prompts of the agent pipeline (generator, checker, tie-breaker, adjudicator and the shared rules) that produce species profiles.
- `validate.mjs`: validator for profile files. `validate.test.mjs` holds its tests.
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

## Cichlid Room Companion

```
xvfb-run -a node tools/species-data/crc.mjs search "Tropheus moorii"
xvfb-run -a node tools/species-data/crc.mjs page "https://cichlidae.com/species.php?id=..."
```

`search` lists matching species, genus and article pages; `page` prints the readable text of one page and its subpage links. Every call starts its own browser, so parallel agents do not share state. All calls share a lock with a minimum interval of 15 seconds, retry twice after a rate limit (Cloudflare error 1015) and cache the printed text for 30 days in `data/species/work/.crc-cache/`.

## Open-access full texts

```
xvfb-run -a node tools/species-data/fetch-oa.mjs 10.3390/fishes11020106
xvfb-run -a node tools/species-data/fetch-oa.mjs https://doi.org/10.3390/fishes11020106
```

The tool gives research agents the text of open-access works. It prints a metadata summary (title, DOI, year, license, open-access status, route, source), the path of the text file and the first 1500 characters.

Library: `data/species/work/.literature/` (ignored by git). Per work it holds `<key>.json` (metadata), the original as `<key>.xml`, `<key>.pdf` or `<key>.html`, and the plain text `<key>.txt`; the key is the lowercase DOI with every character outside `[a-z0-9._-]` replaced by `_`. A work with a text file is served from the library without network access, so each work is downloaded once.

Open-access condition: OpenAlex (API key as for literature-search) must report `open_access.is_oa` as true. The routes are tried in this order, and the metadata field `route` names the one that delivered the text:

1. `openalex-tei`: the TEI XML copy hosted by OpenAlex (`has_content.grobid_xml`); the text holds title, abstract, section heads and paragraphs of the body.
2. `openalex-pdf`: the PDF copy hosted by OpenAlex (`has_content.pdf`), read with `pdfjs-dist`.
3. `publisher-fetch`: the open-access addresses of the work (best location first, then the other open-access locations; PDF address before landing page) with a plain request and a browser user agent.
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
- each hard feature (temperatureC, ph, ghDgh, predation, diet, speciesOnly, hybridGroup) has a verification object;
- a value or tolerance without a source of kind catalog-of-fishes, literature or database (only forum, archive or model knowledge) has confidence low;
- when `features.origin.value.waterbody` matches an entry of `data/species/waterbodies.yaml` (id, name or alias, case-insensitive), temperatureC, ph and ghDgh have verification method `waterbody` with that entry's id, and temperatureC, ph, ghDgh and khDkh equal the entry's ranges exactly. Any verification with method `waterbody` references an existing entry and its value equals that entry's range; the method applies to temperatureC, ph and ghDgh only. A missing table file skips the whole rule with a warning;
- predation, diet, speciesOnly and hybridGroup, and temperatureC, ph and ghDgh unless their verification method is `waterbody`, have `evidence` entries from at least two distinct sources of kind catalog-of-fishes, literature or database (distinct by url, else title);
- temperatureC, ph, ghDgh and khDkh have a `tolerance` (see Tolerance) whose value contains the natural range, whose verification method is unanimous, qualified-majority or conservative (not waterbody), with the evidence rule of the hard features (a conservative tolerance needs one evidence entry of any kind and confidence `low`); no other feature has a tolerance;
- predation, diet, speciesOnly and hybridGroup have at least two distinct sources of kind catalog-of-fishes, literature or database, unless the verification method is `conservative`; a conservative value has confidence `low`;
- id and legacyIds are unique across all validated profiles;
- a hybridGroup value that is not listed in `data/species/hybrid-groups.yaml` is a warning, because agents may propose new groups.

Other files, such as pipeline intermediates under `data/species/work/<id>/`, are checked against the schema only.

The report lists file, JSON pointer and message per issue. The exit code is 1 when at least one error exists and 0 otherwise; warnings do not fail the run.

## Evidence

A fact may carry `evidence`: a list of `{source, value, note?}` entries. `source` has the shape of a profile source, `value` is the value that source states, converted to the unit and shape of the feature (such as `{min: 24, max: 26}` or a level), and `note` is a short qualifier in own words (such as "lake surface water" or "aquarium recommendation"). The species page lists which source states which value when a value is uncertain; it shows values and sources, never copied text. `tally.mjs` collects the entries of all votes and the review page shows them under "Angaben der Quellen".

## Tolerance

Each water feature (temperatureC, ph, ghDgh, khDkh) keeps the natural-habitat range of the species in `value` and carries a `tolerance`: the range in which the species stays healthy long-term according to literature and husbandry experience (care sheets count as sources). A tolerance has the fields of a fact (`value` as a range, `confidence`, `rationale`, `sources`, `evidence`, `verification`). The app warns when the tolerances of two species do not overlap and shows a lesser level when only the natural ranges do not overlap.

## Tally

```
node tools/species-data/tally.mjs data/species/work/<dir>
```

The script reads `generator.yaml`, `checker-a.yaml`, `checker-b.yaml` and every `tiebreaker-N.yaml` of the work directory and writes `tally.yaml` next to them. Tie-breaker files hold only `features` (a subset of the hard features) and optionally `provenance`; they are read without schema validation. Exit code 2 means an input file is missing or the waterbody table is invalid; otherwise the exit code is 0.

Each file that contains a hard feature casts one vote with the feature's `value`. The vote order is generator, checker-a, checker-b, tiebreaker-1, tiebreaker-2, and so on.

- Water body: the most common `features.origin.value.waterbody` over all files is looked up in the table. When it matches, temperatureC, ph, ghDgh and khDkh are the table's ranges with verification `{method: waterbody, waterbody: <id>}`, the table's sources and confidence high; they are not voted on.
- Voted features are predation, diet, speciesOnly and hybridGroup, plus temperatureC, ph and ghDgh when no water body applies.
- Agreement: predation, speciesOnly and hybridGroup by equality (null equals null); diet by `type` and `bloatSensitive`; ranges when both bounds differ by at most 1 (temperatureC), 0.3 (ph) or 2 (ghDgh).
- The top candidate is the proposal with the most agreeing votes; on a tie the earlier file wins. The decided range is the median of the agreeing minima and the median of the agreeing maxima (one decimal for ph, integers otherwise).
- External sources are the distinct sources (by url, else title) of kind catalog-of-fishes, literature or database among the agreeing votes.
- With fewer than 5 votes the feature is `unanimous` when at least 3 votes exist, all of them agree and at least 2 external sources exist. Otherwise it is open with `needsTiebreaker: true` and the reason `disagreement`, `too-few-votes`, `insufficient-sources` or `no-votes`; a tie-breaker agent adds one vote.
- With 5 or more votes the feature is `qualified-majority` when the top candidate has at least 4 agreeing votes and at least 2 external sources. Otherwise it is `conservative` with confidence low: the maximum predation level; speciesOnly true if any vote says true; the most common named hybridGroup; for diet the strictest type (aufwuchs, piscivore, invertivore, omnivore) and bloatSensitive true if any vote says so; for ranges the intersection of all votes, or the median range when the intersection is empty.

Each water feature also gets a tolerance entry named `<feature>.tolerance` (for example `ghDgh.tolerance`), voted from the files' `tolerance` values, also for lake species, because the water body decides the natural value only. The agreement thresholds are 1 (temperatureC), 0.3 (ph), 2 (ghDgh) and 2 (khDkh); the unanimous, tie-breaker and qualified-majority rules are the same. A conservative tolerance is the intersection of all votes (the narrower range triggers more warnings), widened to contain the decided natural range; when the intersection is empty the median range is used and widened the same way. Open tolerance entries appear in `pendingTiebreakers` by their entry name.

Per feature the output has `decision`, `verification`, a `confidence` suggestion (low for conservative; otherwise high with at least 3 external sources and medium with fewer, capped by the median confidence the agreeing agents gave), `needsTiebreaker` and `reason` when open, `proposals` (file and value) and `sources`, and `evidence` (the evidence entries of all votes, once per source and JSON-equal value; empty for water body decisions). The top-level `pendingTiebreakers` lists the open features.

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
