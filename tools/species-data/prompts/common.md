# Species Profile Pipeline: Common Instructions

You work on one species of the cichlids.com catalog. The catalog serves hobbyists who keep or plan
to keep cichlids; the compatibility check of the app warns them about bad tank combinations based
on the features you determine. Wrong values lead to dead fish, so accuracy beats completeness:
when evidence is thin, say so through the confidence and the rationale.

## Read first

- `data/species/rubric.md`: the fixed meaning of every level and value. Use it literally.
- `data/species/schema/species-profile.schema.json`: the file format.
- `data/species/hybrid-groups.yaml`: the curated hybrid groups.

## Research tools

Web research is mandatory. WebSearch and WebFetch are deferred tools: load them first with
ToolSearch (query `select:WebSearch,WebFetch`). `curl -sL <url>` through Bash works as well and
often gets through where WebFetch is blocked. Fetch and read the pages you cite; a source you did
not read is not a source.

Literature search: `node tools/species-data/literature-search.mjs "<query>" [limit]` searches
OpenAlex (with an API key), Europe PMC and Crossref and prints year, title, DOI, an open-access
link and the abstract of each work. Use it first for scientific literature. WebSearch is shared by all agents of the session
(200 searches, refilling at about 100 per hour); use it sparingly for what the literature search
and direct page fetches cannot find.

Full texts of scientific works: `xvfb-run -a node tools/species-data/fetch-oa.mjs <doi>` returns the
text of an open-access work. It looks in the local library first, then takes the copy that
OpenAlex hosts and only then the publisher page. It refuses works that are not open access. Read
the text file it names. Every download costs budget, so fetch only works you will cite.

Access rules: logins, paywalls and captchas are never bypassed. A bot check in front of a work
that is free to read is passed only through `fetch-oa.mjs` (and the Cichlid Room Companion through
`crc.mjs`); do not drive browsers or change user agents yourself. When a page stays blocked, use
another source and say in the rationale what you could not read.

Minimum research per species:

- the Catalog of Fishes entry (see Taxonomy),
- the FishBase species summary (https://www.fishbase.se/summary/<Genus>-<species>.html),
- at least two hobby or literature sources with species-specific husbandry data (for example
  Seriously Fish, Practical Fishkeeping, Malawi or Tanganyika specialist sites, published
  articles),
- a search of the cichlids.com forum archive.

## Cichlid Room Companion

The Cichlid Room Companion (cichlidae.com) serves for verification only: it confirms or questions
values found in other sources, it is never the origin of a value and never a template for text.
Copy nothing from it, neither sentences nor structure. Its detailed profiles are for subscribers;
the public part of a species page holds the classification, the summary lines for morphology,
habitat, feeding, breeding and aquarium level, a short abstract, the original description and
the subspecies. Use it to check the valid name, the feeding type, the breeding mode and the
habitat. When it contradicts a value, say so in the rationale and weigh the evidence.

The site blocks curl and WebFetch. Read it with the project tool:
`xvfb-run -a node tools/species-data/crc.mjs search "<scientific name>"` lists matching pages,
`xvfb-run -a node tools/species-data/crc.mjs page "<url>"` prints the readable text of one page.
The site often uses older genus names (for example Metriaclima), so search by the species epithet
alone. All agents share one throttled connection and a cache, so a call can take a minute; one
search and one page per species are enough.

`model-knowledge` as the only source of a value is allowed only with confidence `low`.

## Taxonomy

- The authority for valid names and synonyms is the Catalog of Fishes of the California Academy of
  Sciences (Eschmeyer's Catalog of Fishes). Its species list for a genus is plain HTML:
  `curl -sL "https://researcharchive.calacademy.org/research/ichthyology/catalog/fishcatget.asp?tbl=species&genus=<Genus>" | sed 's/<[^>]*>//g'`.
  Every entry ends with "Current status: Valid as ..." or "Current status: Synonym of ...". Query
  the legacy genus as well as the current one, record the URL and the access date as the taxonomy
  source.
- The legacy catalog name may be outdated (for example Cichlasoma octofasciatum is Rocio
  octofasciata). Use the valid name for `id`, `taxonomy.validName`, `taxonomy.genus` and
  `taxonomy.species`; list outdated names under `taxonomy.synonyms` and the legacy spelling under
  `taxonomy.legacyNames`.
- Undescribed species with a trade name (`sp. 'Locality'`) get the status `undescribed`. Crosses
  get `hybrid`, captive lines such as Flowerhorn get `domestic-form`.
- If the Catalog of Fishes is not reachable, use another reliable taxonomic source (FishBase
  nomenclature, a recent revision paper), say so in `taxonomy.note` and lower the confidence.

## Sources

- Source kinds: `literature` for papers, books, magazine articles and reports; `database` for
  species databases (FishBase, Seriously Fish) and structured care sheets; `website` for Wikipedia,
  blogs and other general pages. Only `catalog-of-fishes`, `literature` and `database` count as
  external sources; `website`, `forum`, `archive` and `model-knowledge` carry low weight.
- Every value needs at least one source. Peer-reviewed papers and monographs with a DOI are
  especially welcome: search for them deliberately (species descriptions, ecology, diet and
  limnology studies), read them and cite them with `https://doi.org/<doi>` as url; a paper of
  which only the abstract was readable supports only what the abstract states. Prefer primary literature, species databases (Seriously
  Fish, FishBase), books and well-regarded hobby sources. Record title and
  URL. Use `model-knowledge` only when no external source confirms a value, and then never with
  confidence `high`.
- The cichlids.com archive is a source of practical experience. Search the forum posts with
  `node tools/species-data/archive-search.mjs "<name>" [limit]`; it prints each matching thread with
  its legacy thread id, its URL and excerpts of the posts. Cite archive findings as kind `archive`
  with exactly the printed title `cichlids.com forum thread <legacy id>` and the printed `url`.
  Archive and forum posts are community experience with very low weight: list them after the
  literature and database sources, and let a value rest on them only when no source of kind
  `catalog-of-fishes`, `literature` or `database` states it; such a value has confidence `low`.
  Never copy member names or handles into any file.
- Record in `evidence` which source states which value: one entry per source with the value it
  states, converted to the unit and shape of the feature, and an optional short note in your own
  words (for example "lake surface water"). The app shows these entries when a value is uncertain.
  Hard features need at least two entries from sources of kind `catalog-of-fishes`, `literature`
  or `database`; give evidence for the other features wherever a source states a value.
- Copyright is binding. Foreign texts are evidence for facts and numbers only. Never quote
  verbatim: no sentence, part of a sentence, list or definition from Seriously Fish, forums or any
  other source, not even short and not even in quotation marks. Write all prose and rationales in
  your own words and your own order, and never follow the structure of a source.

## Values

- Metric units: centimetres, litres, degrees Celsius, dGH, dKH.
- Water values (temperatureC, ph, ghDgh, khDkh) are the values measured at the natural habitat.
  For species of Lake Malawi, Lake Tanganyika and Lake Victoria they are exactly the lake values in
  `data/species/waterbodies.yaml`; copy them with that file as source. For other species use
  measurements from the habitat (limnological papers, collecting reports, FishBase environment
  data). Aquarium care sheets are no source for water values: many of them describe Lake Malawi as
  hard water, which confuses hardness with alkalinity. Hybrids and domestic forms take the habitat
  values of their parent species.
- Each water feature also has a `tolerance`: the range in which the species stays healthy for years
  in the aquarium, according to husbandry literature, care sheets, breeders and the archive. Here
  care sheets count as sources. The tolerance contains the natural range. It is a fact of its own
  with value, confidence, rationale, sources and evidence, and it needs at least two sources of kind
  `literature` or `database`. Do not write a `verification` object for it either.
- Maximum size is the typical maximum of well-kept adult aquarium fish per sex, not record sizes.
- `minTank` is for the recommended social unit of adults (for example one male with three females).
- Hard features (water values, predation, diet, species-only and hybrid group) are verified by
  counting the votes of independent agents. Predation, diet, species-only and hybrid group each need
  at least two sources of kind `catalog-of-fishes`, `literature` or `database`; forum and archive
  posts support them but do not count towards these two. Do not write a `verification` object;
  the vote count adds it.

## Output

Keep downloads and notes in `data/species/work/<your work directory>/tmp-<your role>/` and never
read another role's temporary files; the shared scratchpad of the session is off limits.

Write YAML that validates against the schema, with comments only where a value needs an explanation
beyond its rationale. Use the date of today for access dates and runs.
