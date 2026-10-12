# Knowledge Article Pipeline: Common Instructions

Knowledge articles explain topics beyond a single species for the hobbyists of cichlids.com, for
example the water chemistry of Lake Malawi and why aquarium care sheets differ from it. The species
pages link to them, and the AI first answer of the app draws on them. Wrong statements mislead
keepers, so every statement needs a source that states it.

## Format

An article is the file `data/knowledge/articles/<id>.md`: YAML frontmatter following
`data/knowledge/schema/article.schema.json`, then a Markdown body in English. Read the schema and
the section "Knowledge articles" in `tools/species-data/README.md` first. Validate with
`node tools/species-data/validate-articles.mjs <file>` until it reports 0 errors.

- Cite with Markdown footnote references `[^key]`; `key` is a key of `sources`. Every paragraph and
  every list item carries at least one citation, placed after the statement it supports.
- Write for interested hobbyists: plain language, short paragraphs, concrete numbers with units
  (metric, with dGH and dKH for hardness), headings that say what the section answers.
- Explain causes and consequences: what the facts are, why they are so, why common advice differs,
  and what follows for the aquarium. Where sources disagree, say which source states what.
- Length: 600 to 1200 words.

## Sources and copyright

Web research is mandatory. WebSearch and WebFetch are deferred tools: load them with ToolSearch
(query `select:WebSearch,WebFetch`); `curl -sL <url>` works as well. Read every page you cite.
Literature search: `node tools/species-data/literature-search.mjs "<query>" [limit]` searches
OpenAlex (with an API key), Europe PMC and Crossref and prints year, title, DOI, an open-access
link and the abstract of each work. Use it first for scientific literature. WebSearch is shared by all agents of the session
(200 searches, refilling at about 100 per hour); use it sparingly for what the literature search
and direct page fetches cannot find.

Full texts of scientific works: `xvfb-run -a node tools/species-data/fetch-oa.mjs <doi>` returns the
text of an open-access work. It looks in the local library first, then takes the copy that
OpenAlex hosts and only then the publisher page. It refuses works that are not open access. Read
the text file it names; the shared library `data/species/work/.literature/` is open to every role. Every download costs budget, so fetch only works you will cite.

Access rules: logins, paywalls and captchas are never bypassed. A bot check in front of a work
that is free to read is passed only through `fetch-oa.mjs` (and the Cichlid Room Companion through
`crc.mjs`); do not drive browsers yourself and do not set browser user agents or other client
headers (for example `curl -A`). When a page stays blocked, use
another source and say in the rationale what you could not read.

Prefer primary literature and limnological data. Peer-reviewed papers, monographs and research
reports with a DOI are especially welcome: search for them deliberately, read them (open-access
version, abstract or preprint) and cite them with `https://doi.org/<doi>` as url; a paper of which
only the abstract was readable supports only what the abstract states; hobby sources serve to show what the hobby
recommends. `data/species/waterbodies.yaml` holds the curated lake values with their sources; use
these numbers and cite the underlying studies. Files of this repository (tables, profiles,
prompts) are never a source. The cichlids.com forum archive is searchable with
`node tools/species-data/archive-search.mjs "<term>"`; forum posts are community experience with
very low weight that supplements literature and supports a statement on its own only when no
other source exists, and the text then says that keepers report it; cite it with the title and url the tool
prints, and never copy member names or handles. The Cichlid Room Companion serves for verification
only (see `tools/species-data/prompts/common.md`).

Copyright is binding. Foreign texts are evidence for facts and numbers only:
- Never quote verbatim: no sentence, part of a sentence, list, ingredient list, definition or table
  from a source, not even short and not even in quotation marks.
- Always write in your own words and your own order; understand the fact first, then phrase it
  without the source text in view. Do not follow the structure of a source.
- Every statement names its source; never take over images or graphics.

Keep downloads and notes in `data/knowledge/work/<id>/tmp-<your role>/` and never read another
role's temporary files.

Writing style: present tense, no em or en dashes (use commas, colons, parentheses or hyphens), no
filler or buzzwords, varied sentence length.
