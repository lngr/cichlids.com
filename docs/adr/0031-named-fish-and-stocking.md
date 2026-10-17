# ADR-0031: Named Fish and Stocking

- **Status:** Proposed
- **Date:** 2026-10-17
- **Deciders:** Alexander Langer

## Context and Problem Statement

A tank lists what lives in it. For most keepers that is a stocking list: twelve Mbuna of one
species and a group of catfish. Many keepers also know single fish by name, follow how
they grow and keep their story long after they died. The platform wants both: a stocking list that
the care plan, the stocking planner and the Fish Log read, and a page for a single fish with its own
photos and timeline that survives a move to another tank and its death.

ADR-0029 attaches media and story references to an inhabitant of a tank and leaves open how a
single fish gets a name, an identity and a page. The Fish Log counts how long a member has kept a
species and a named fish, which species the member bred and in which generation. Those numbers need
a history of the stocking, with dates, beyond the list of what is in the tank on one day.

The question is how a tank's stocking, a named fish and their history are modelled, so that
media, stories, care entries and the Fish Log refer to one thing.

## Decision

**One inhabitant per stocking row.**

- An inhabitant is either a group (a species and a count) or a named fish (a species, a count of
  one and a name). Both live in one inhabitant table, so media links and story subjects of
  ADR-0029 reach groups and named fish alike.
- The owner of a tank adds, edits and removes inhabitants. Each has the day it moved in and, once
  it left, the day and the reason: it died, was given away, or moved to another tank. A removed
  inhabitant stays as history of the tank; only the current ones count as the stocking.
- The species is optional for a named fish whose species the keeper does not know.

**A named fish keeps its identity.**

- A named fish belongs to its member and has a current tank. Moving it to another tank of the same
  member changes its tank and records the move; its page, photos and timeline stay one.
- After its death the page stays as a memorial with its dates. Deleting a tank keeps the pages of
  the named fish that lived in it.
- A named fish page is visible to the viewers of its current tank, or of the last tank it lived in.
  Its media links follow that visibility.

**A timeline of events.**

- Events of a named fish are rows of their own: moved in, growth with a length, spawning with the
  offspring's generation, a move between tanks, death. Anniversaries follow from the dates and are
  not stored.
- A care entry or a story of the tank can name inhabitants. The entry then appears on the tank and
  on the timeline of each named fish.

**The Fish Log reads the history.**

- Kept species and keeping time come from the inhabitant history of the member's tanks. Bred
  species and their generation come from spawning events whose offspring is pure-bred. A wishlist is
  a list the member keeps of its own.

## Considered Options

- **Named fish as inhabitants with a name (chosen).**
  - *Pro:* One context for media and stories; the stocking list and the fish pages cannot drift
    apart.
  - *Con:* A move between tanks touches a row that also belongs to the tank's history, so the move
    is an event of its own.
- **A fish table beside the inhabitant groups.**
  - *Pro:* A group and a single animal are different things in the model.
  - *Con:* Two contexts for media and story references, and a named fish that is also counted in
    a group of the same species.
- **Names as free text on a group.**
  - *Pro:* Small.
  - *Con:* No page, no timeline, no identity across tanks.

## Rationale

The stocking list and the named fish describe the same animals. Keeping them in one table with a
history gives the care plan, the stocking planner and the Fish Log one source, and gives a named
fish everything ADR-0029 offers a context without new media rules. Dates on every row make the
keeping time and anniversaries computable from the data.

## Consequences

- **Positive:** A keeper writes the stocking once and gets the planner's hints, the Fish Log and
  pages for the fish that matter to them. Photos of a named fish are one set wherever it lives.
- **Negative / Trade-offs:** The visibility of a named fish moves with it: photos taken in a
  private tank become visible with the fish when it moves into a public tank. The move dialog says
  so.
- **To be decided later:** Following a single named fish, transfers of a fish between members, and
  how the Fish Log weighs experience per species.

## References

- [ADR-0029: Media Links and Stories](0029-media-links-and-stories.md)
- [ADR-0030: Provisional Device Accounts](0030-provisional-device-accounts.md)
