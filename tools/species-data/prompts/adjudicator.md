# Role: Adjudicator

You receive the generator's full profile, the independent checkers' results, the tie-breaker
results if any, and `tally.yaml`, the vote count for the hard features. Produce the final profile
file:

1. When `tally.yaml` lists pending tie-breakers, stop and report them; profile output requires
   a settled tally. Take the decided values, `verification` objects and confidence from `tally.yaml`,
   subject to the water plausibility check below. Keep every source and evidence entry from every
   input, including dissenting votes; write a rationale that explains the decided value from these
   sources. Trust from `data/species/source-trust.yaml` guides confidence and conflict weight, never
   the retention of a source or the validity of a value. A hard feature or tolerance has confidence
   `high` only with at least one high-trust evidence source; otherwise use `medium` or `low`.
   For predation, diet, speciesOnly and hybridGroups, keep the tally decision. If any of these
   decisions is implausible, name it and the reason at the start of your report with the words
   PLAUSIBILITY CHECK.

   Check every decided natural water range and tolerance (temperatureC, ph, ghDgh, khDkh) for
   sense in aquarium practice. Natural ranges describe the typical waters of the species
   relevant for keeping. Describe exceptional habitats with chemistry no aquarist provides,
   such as a gypsum karst lagoon, in `profile.habitat` with their reported values; keep these
   values out of the natural range and the tolerance. Replace an implausible tally range, such
   as a median between two camps of votes or a range stretched by an exceptional habitat, with
   a plausible range and verification method `adjudicated`. Keep the tally's `votes`, record its
   `decision` range as `verification.replaced` and its method as `verification.replacedMethod`
   (unanimous, qualified-majority, conservative or median). Name the replaced decision and the
   reason in the rationale. Set confidence to `low` or `medium` and provide at least one evidence
   entry of any trust level. Keep each tolerance wide enough to contain the decided natural range. Never remove a source or an
   evidence entry: keep those whose values lie outside the adjudicated range, with the value the
   source states.

   For a species whose origin names a water body in `data/species/waterbodies.yaml`, keep its
   natural table ranges and verification method `waterbody` for temperatureC, ph and ghDgh.
   Natural water values decided by a water body get evidence entries from the lake studies behind
   `data/species/waterbodies.yaml`; cite these studies, never the table file itself. Every water
   feature carries the `tolerance` the tally decides, subject to the water plausibility check.
   With 5 or more votes and no qualified majority, a tolerance has method `median` and confidence
   `low`: the median of all minima and the median of all maxima, rounded to one decimal for ph
   and to integers for temperatureC, ghDgh and khDkh, widened to contain the decided natural range.
   The intersection lets the single narrowest proposal set the limit and produces water conflicts
   absent from aquarium practice; the median range reflects what the sources recommend.
   For every group id in the `hybridGroups` the tally decides: when `data/species/hybrid-groups.yaml`
   does not list the group, add it there with `id`, a one-sentence `description`, the valid names of
   its `members`, the `sources` (literature or database) that report crosses, a `userNote` and its `basis`:
   `documented` when a source reports a cross between named members from the wild or from a tank
   where the fish could choose partners of their own species, otherwise `presumed` (general
   warnings such as "the species of this genus cross", or a very close relationship). A forced
   laboratory cross, such as females kept without a male of their own species, gives a two-member
   group with the basis `presumed`, and so does a report of a cross between two specific species
   where free choice of partners is not shown. The `userNote` is one or two
   plain-English sentences for hobbyists that explain the background of the group and give no
   instructions: what was observed (wild hybrids, a tank report, only a general warning about the
   genus, a forced laboratory cross), where and by whom where known, and how
   reliable that evidence is, for example "A single hobby source mentions an aquarium cross of these
   two species without naming its circumstances, so the risk is plausible but unconfirmed." When the group
   exists but lacks this species, add the species to its members. A species may be a member of
   several groups.
2. Compare taxonomy and every other feature value across all files. Values agree when they are
   equal, or for ranges when the bounds differ by at most 1 degree Celsius, 0.3 pH or 2 dGH.
3. Where they agree, keep the value, merge the sources and raise the confidence where independent
   sources support it.
4. Where they differ, weigh the rationales and sources (verify them with web search where useful)
   and decide. In a conflict, the higher trust source wins unless the lower trust source reports a
   direct field measurement and the higher trust source gives a general recommendation. Record every
   difference in `provenance.disagreements` with the field, all proposals, your decision and the reason. Each proposal is `{by: <run>, value: <value>}`, where
   the run is `generator`, `checker-a`, `checker-b`, `checker-c` or `tiebreaker-<n>`. Record the disagreements of hard features as well,
   with the decision and method from `tally.yaml`, or your adjudicated range and the reason for
   replacing the tally decision.

   For the numbers of soft features (`maxSizeCm`, `minTank` volume, length and depth,
   `lifespanYears`), start from the median of the proposals and check it against the values the
   cited sources state. A value above the largest source value needs a reason in the rationale,
   such as the recommended social unit; the largest proposal alone is no reason.
5. Make the profile prose, `partners` and the final features consistent with each other; rewrite
   prose that contradicts a decided value.
6. Merge the `provenance.runs` of all inputs and add your own run of role `adjudicator`.

Write the result to the final path given in your task. It must validate against the schema.
