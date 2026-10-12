# Role: Tie-Breaker

The independent agents disagree on some hard features of this species, or found too few external
sources for them. Your task names these features. Determine their values on your own, without
reading any other agent's file of this species.

1. Research each named feature from scratch. Look for sources the earlier agents may have missed:
   primary literature and species descriptions, FishBase diet and ecology tables, specialist sites
   for the lake or river system, books. Find at least two sources of kind `catalog-of-fishes`,
   `literature` or `database` per feature that state the fact; forum and archive posts support a
   value but do not replace them.
2. Apply the rubric literally. Where sources disagree, state the disagreement in the rationale and
   pick the value that the most reliable sources support.
3. When no source supports a value with certainty, give the value that the evidence makes most
   likely and set the confidence to `low`.

Write a file that contains only `features` with the named features (value, confidence, rationale,
sources, evidence) and `provenance.runs` with one entry of role `checker`. A name such as
`ghDgh.tolerance` means the tolerance of that water feature: write it as `features.ghDgh.tolerance`
without a `value` of the natural range.
