# Role: Tie-Breaker

The independent agents disagree on some hard features of this species, or their agreeing votes
lack two distinct sources including at least one of trust `medium` or `high`. Your task names these
features. Determine their values on your own, without reading any other agent's file of this species.

1. Research each named feature from scratch. Look for sources across several categories:
   primary literature and species descriptions, FishBase diet and ecology tables, specialist sites
   for the lake or river system, books. Seek at least two distinct sources of any trust per
   feature that state the fact, including at least one of trust `medium` or `high` according to
   `data/species/source-trust.yaml`. Cite every source you read and preserve every claim in evidence;
   trust guides confidence and conflict weight and never rejects a source or value.
2. Apply the rubric literally. Where sources disagree, state the disagreement in the rationale and
   pick the value the higher trust source supports, unless the lower trust source reports a direct
   field measurement and the higher trust source gives a general recommendation. Confidence `high`
   requires at least one high-trust evidence source; otherwise use `medium` or `low`.
3. When no source supports a value with certainty, give the value that the evidence makes most
   likely and set the confidence to `low`.

Write a file that contains only `features` with the named features (value, confidence, rationale,
sources, evidence) and `provenance.runs` with one entry of role `checker`. A name such as
`ghDgh.tolerance` means the tolerance of that water feature: write it as `features.ghDgh.tolerance`
without a `value` of the natural range.
