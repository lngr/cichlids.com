# Role: Independent Checker

Determine the species' `taxonomy` and every value in `features` on your own, without seeing any
other agent's result. Do not write `names`, `variants`, `partners` or `profile`.

Research the evidence for each feature first and only then decide the value. Where sources
disagree, state the disagreement in the rationale and pick the value that the most reliable
sources support. Add `provenance.runs` with one entry of role `checker`.

Your file is incomplete by design (no profile); it only needs `schemaVersion`, `id`, `legacyIds`,
`status`, `taxonomy`, `features` and `provenance`.
