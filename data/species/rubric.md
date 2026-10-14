# Species Feature Rubric

Every graded feature of a species profile uses the fixed level descriptions below, so that
generating, checking and tie-breaking agents assign the same level to the same
behaviour. A level always describes adult fish kept in a tank that meets the species' minimum
tank and recommended social structure.

Units are metric: centimetres, litres, degrees Celsius, dGH and dKH. The app converts them for
display.

## Water values (`temperatureC`, `ph`, `ghDgh`, `khDkh`)

Natural ranges describe the typical waters of the species relevant for keeping. Exceptional
habitats with chemistry no aquarist provides, such as a gypsum karst lagoon, are described in
`profile.habitat` with their reported values and stay out of the natural range and the tolerance.
The tolerance describes the range in which the species stays healthy long-term according to
literature and husbandry experience and contains the natural range.

The adjudicator checks every decided water range and tolerance for sense in aquarium practice.
A median between two camps of votes or a range stretched by an exceptional habitat can be
implausible. The adjudicator replaces an implausible tally decision with method `adjudicated`,
records the tallied range in `verification.replaced`, its method in `verification.replacedMethod`
and its vote count in `verification.votes`, and names the replaced decision and the reason in the
rationale. An adjudicated range has confidence `low` or `medium` and at least one evidence entry
of any kind. For a species whose origin names a water body in the table, the natural values keep
the table ranges and temperatureC, ph and ghDgh keep verification method `waterbody`.

## Predation (`predation`, 0 to 3)

| Level | Meaning | Example |
|---|---|---|
| 0 | Does not eat fish; fry may be picked up only by chance. | Tropheus moorii |
| 1 | Eats fry and fish up to about 2 cm when the chance arises. | Labidochromis caeruleus |
| 2 | Regularly eats any fish that fits into its mouth. | Cyphotilapia frontosa |
| 3 | Pronounced piscivore; fish are a main part of its diet. | Parachromis dovii |

The compatibility check combines this level with the maximum size: a fish counts as prey when its
maximum size is below the predator's mouth-size threshold (one third of the predator's maximum
size for levels 2 and 3, 2 cm for level 1).

## Aggression towards conspecifics (`aggressionConspecific`, 0 to 4)

| Level | Meaning |
|---|---|
| 0 | Peaceful; lives in a group without lasting disputes. |
| 1 | Displays and short chases; no injuries. |
| 2 | Clear hierarchy with regular chasing; injuries are rare with enough space and the recommended ratio. |
| 3 | Persistent chasing of rivals or females; injuries are common with too little space, cover or the wrong ratio. |
| 4 | Kills or permanently injures rivals or partners unless kept in a large group, a large tank or with separation; keeping it requires experience. |

## Aggression towards other species (`aggressionHeterospecific`, 0 to 4)

| Level | Meaning |
|---|---|
| 0 | Ignores other species. |
| 1 | Defends its immediate surroundings by displaying. |
| 2 | Chases other species out of its territory; no injuries in a suitable tank. |
| 3 | Attacks other species beyond its territory; tankmates need comparable size and robustness. |
| 4 | Injures or kills fish of other species of similar size; suitable only alone or with very robust tankmates in very large tanks. |

## Territory (`territory`)

| Value | Meaning |
|---|---|
| `none` | Claims no area. |
| `small` | Claims a cave, a shell or a spot of about 30 cm around it. |
| `large` | Claims a substantial part of the tank, about a third of the length of a 150 cm tank or more. |

The flag `breedingOnly` marks species that claim the territory only while breeding.

## Feeding speed (`feedingSpeed`)

| Value | Meaning | Example |
|---|---|---|
| `slow` | Picks food deliberately and loses it to faster tankmates. | Symphysodon |
| `normal` | Takes its share at a normal pace. | Cyrtocara moorii |
| `hasty` | Rushes food and eats until nothing is left. | Mbuna such as Maylandia |

## Diet (`diet`)

| Value | Meaning |
|---|---|
| `aufwuchs` | Grazes algae and biofilm; needs a plant-based, low-protein diet. |
| `omnivore` | Eats plant and animal food. |
| `invertivore` | Mainly eats insects, crustaceans, snails or zooplankton. |
| `piscivore` | Mainly eats fish. |

The flag `bloatSensitive` marks species that tend to develop bloat on a high-protein diet, so that
a tankmate that needs such a diet is a conflict.

## Habitat zones (`habitatZones`)

A set of `rock`, `sand`, `openWater`, `shell`, `plants` and `wood`: the zones the species uses in
the wild and needs in the tank.

## Social structure (`social`)

| Value | Meaning |
|---|---|
| `pair` | Forms a pair; other adults of the species are rivals. |
| `harem` | One male with several females; `ratio` gives the recommended number of females per male. |
| `group` | Lives in a group; `minGroup` gives the smallest group that spreads aggression. |
| `solitary` | Kept as a single fish. |

The flag `speciesOnly` marks species that the hobby recommends to keep only with their own
species, such as Tropheus.

## Coloration (`coloration`)

`baseColors` lists the dominant colours of the adult male (such as `blue`, `yellow`, `red`,
`orange`, `white`, `black`, `brown`, `grey`, `green`, `silver`). `pattern` is one of `bars`
(vertical), `stripes` (horizontal), `spots`, `plain` or `mixed`. Coloration counts towards conflicts
because similar-looking species fight like rivals of their own species and cross more easily.

## Hybrid groups (`hybridGroups`)

The list of identifiers of the curated groups of species that cross in tanks, such as
`maylandia-zebra-complex`, and the empty list for a species without such a group. A group means that
every member may cross with every other member. A documented cross of two species without a wider
group is a group of two members. Crossability is not transitive (A crosses with B and B with C, but
A not with C), so a species belongs to every group whose crosses concern it. The groups and their
members are listed in `hybrid-groups.yaml`. Two members that share at least one group are in hybrid
conflict in one tank, and the stocking planner tells the user that the fish may cross.

Every group has a `basis` that states the strength of its evidence and sets the level of the conflict
in the stocking planner:

| Basis | Evidence | Conflict of two members in one tank |
|---|---|---|
| `documented` | At least one cross between named members is reported from the wild or from an aquarium where the fish could choose partners of their own species. | Hard (warning). |
| `presumed` | Only general warnings exist, such as hobby sources saying that the species of a genus cross and must not share a tank, or a very close relationship. | Medium (caution). |

A forced laboratory cross, such as females kept without a male of their own species, gives a group of
those two species with the basis `presumed`: it shows fertile offspring, and in hobby tanks a female
without a fitting male of her own species is common. A report of a cross between two specific species
where free choice of partners is not shown also gives a group of those two species with the basis
`presumed`.

A member is a valid species name or the trade name of a domestic hybrid line, such as Flowerhorn or
the blood parrot cichlid. Keepers put such lines into community tanks as well, and the risk of an
unwanted cross is the same. A domestic line is a member when a cross between it and a species is
reported; the group then names the line itself and leaves the species in its unknown ancestry out.

Every group has a `userNote`: one or two plain-English sentences for hobbyists, shown next to the
caution or warning. The note explains the background of the group and gives no instructions. It
says what was observed (wild hybrids, a tank report, only a general warning about the genus, a forced
laboratory cross), where and by whom where known, and how reliable that evidence
is, so that a reader understands why the planner shows the caution or warning. Example: "Wild hybrids
of these two species were found at Thumbi West Island after Cynotilapia afra was introduced there;
genetic studies confirm the mixing."

## Minimum tank (`minTank`)

The smallest tank for the recommended social unit of adult fish: volume in litres, length and
depth in centimetres.

## Confidence

Every value carries a confidence:

| Value | Meaning |
|---|---|
| `high` | Several independent, reliable sources agree. |
| `medium` | One reliable source, or sources agree only roughly. |
| `low` | Derived from related species or general knowledge; needs confirmation. |
