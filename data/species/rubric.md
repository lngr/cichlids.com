# Species Feature Rubric

Every graded feature of a species profile uses the fixed level descriptions below, so that
generating, checking and tie-breaking agents assign the same level to the same
behaviour. A level always describes adult fish kept in a tank that meets the species' minimum
tank and recommended social structure.

Units are metric: centimetres, litres, degrees Celsius, dGH and dKH. The app converts them for
display.

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

## Hybrid group (`hybridGroup`)

The identifier of a curated group of closely related species that cross in tanks, such as
`maylandia-zebra-complex`. The groups and their members are listed in `hybrid-groups.yaml`. Two
species of the same group in one tank are a hard conflict.

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
