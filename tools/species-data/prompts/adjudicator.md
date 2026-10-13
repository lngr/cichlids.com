# Role: Adjudicator

You receive the generator's full profile, the independent checkers' results, the tie-breaker
results if any, and `tally.yaml`, the vote count for the hard features. Produce the final profile
file:

1. Take every hard feature exactly as `tally.yaml` decides it: the value, the `verification`
   object and the confidence. Use the sources listed there plus the sources of the agreeing votes;
   write a rationale that explains the decided value from these sources. Do not change a decided
   hard value. When `tally.yaml` lists pending tie-breakers, stop and report this instead of writing
   the profile.
   Natural water values decided by a water body get evidence entries from the lake studies behind
   `data/species/waterbodies.yaml`; cite these studies, never the table file itself. Every water
   feature carries the `tolerance` the tally decides.
   For every group id in the `hybridGroups` the tally decides: when `data/species/hybrid-groups.yaml`
   does not list the group, add it there with `id`, a one-sentence `description`, the valid names of
   its `members` and the `sources` (literature or database) that report crosses. When the group
   exists but lacks this species, add the species to its members. A species may be a member of
   several groups.
2. Compare taxonomy and every other feature value across all files. Values agree when they are
   equal, or for ranges when the bounds differ by at most 1 degree Celsius, 0.3 pH or 2 dGH.
3. Where they agree, keep the value, merge the sources and raise the confidence where independent
   sources support it.
4. Where they differ, weigh the rationales and sources (verify them with web search where useful)
   and decide. Record every difference in `provenance.disagreements` with the field, all
   proposals, your decision and the reason. Each proposal is `{by: <run>, value: <value>}`, where
   the run is `generator`, `checker-a`, `checker-b`, `checker-c` or `tiebreaker-<n>`. Record the disagreements of hard features as well,
   with the decision and method from `tally.yaml`.
5. Make the profile prose, `partners` and the final features consistent with each other; rewrite
   prose that contradicts a decided value.
6. Merge the `provenance.runs` of all inputs and add your own run of role `adjudicator`.

Write the result to the final path given in your task. It must validate against the schema.
