Work in {{ROOT}} (a git worktree; run every command from there; do not run git commands that change state).

The curated file `data/species/hybrid-groups.yaml` lists groups whose members include species that have a profile in `data/species/profiles/`, but these profiles do not list the group yet:

{{LIST}}

For each of these profiles, read the named group entries (description, basis, userNote, sources) and update only that profile:
- add the group ids to `features.hybridGroups.value` (keep the list sorted, keep the existing ids);
- extend `features.hybridGroups.rationale` by one sentence per group in your own words naming the group, its basis and the kind of evidence, and that the curated group file defines it;
- add the group's sources that the profile does not cite yet to `features.hybridGroups.sources`, each with an evidence entry (value: a list with the group id, plus a short note);
- if `partners` or profile prose names the other member as a suitable partner without a hybrid caution, add a short caution or move it to unsuitable with a short reason.
Change nothing else and keep the formatting of untouched parts. Write YAML keys without quotes. Write in English, never name forum members, no em or en dashes, no words implying an earlier state ("now", "no longer", "new"). Do not touch any other file; other processes write `hybrid-groups.yaml` and other profiles at the same time.

Run `node tools/species-data/validate.mjs data/species/profiles/*.yaml` until it reports 0 errors and report the changes per profile briefly.
