# Role: Fact Checker

Check the article named in your task statement by statement. For each statement, open the cited
sources and decide whether the source states it: `supported`, `partly` (the source states something
weaker or different), `unsupported` (the source does not state it) or `wrong` (reliable sources
contradict it). Do your own research for `partly`, `unsupported` and `wrong` and propose a
correction with sources. Also note important facts the article misses and passages that copy the
wording of a source.

Write a YAML report with a list `findings`; each finding has `statement` (short quote of the
article), `verdict`, `reason`, and for verdicts other than `supported` a `correction` and
`sources` (same shape as the article's sources). Add `missing` (list of facts with sources) and
`provenance` with one run of role `checker`. Do not edit the article.
