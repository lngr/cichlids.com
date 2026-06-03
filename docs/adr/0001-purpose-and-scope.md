# ADR-0001: Purpose and Scope of the Application

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

cichlids.com is a large, English-language **photo-sharing and rating community for cichlid
enthusiasts** ("Instagram for cichlids") that grew since the 2000s and has been offline
since 2023. The legacy application's data and source code have been fully recovered: roughly
189,000 images, 1.6 million comments, nearly 12,000 user accounts, and about 6,700
documented aquariums.

The defining trait of this community: members invest **a great deal of time, money, and
care** into their aquariums and fish stock, and want to show that work and receive
**recognition** for it. The legacy application was web-centric and at its core only allowed
uploading **single** images. Members today increasingly capture **video**, not just photos;
the legacy application matches neither this reality, nor today's mobile-first usage, nor the
motive of pride and recognition.

A clear, binding definition is therefore needed of **what** the revived application is and
does.

## Decision

We build a **free, mobile-first media-sharing community** (photos **and** video) in which a
member's own aquarium (**tank**) is **at the center** and members earn **recognition for their
husbandry work**.

**Primary goal:** maximum, immediate value for the community and the rebuilding of a large,
free user base. **No monetization** in this stage.

### Guiding principle

The entire application is geared toward making people's work on their tanks and fish
**visibly recognized**. Profile and tank are tightly linked: a member's aquariums are the
**core of their self-presentation**, not a side aspect.

### Functional scope

- **Tank as a first-class object** — every member presents their aquariums; the profile
  foregrounds the member's own tanks.
- **Media and stories** — **photos and video** are first-class and equal: single photos and
  single videos, as well as multi-part stories that combine photos and video about a tank or a
  fish.
- **Recognition / reputation** — ratings, badges, leaderboards, and curation ("Top-rated",
  "Interesting") make husbandry work visible and reward it.
- **Social** — following, feed, comments, collections/pins.
- **Species/taxonomy** — linking images and tanks to cichlid species.
- **Discover/Explore** — highlights and thematic entry points.
- **Legacy content carry-over** — the recovered content (images, tanks, comments, profiles)
  is migrated into the new application, so the existing community finds its history again.
- **Account deletion & data export** — members can export their data and fully delete their
  account.

### Platform orientation

- **Mobile-first.** Native apps for **Apple App Store** and **Google Play** — free and
  compliant with store review requirements. The app serves as a convenient interface for
  camera, capture, and the (also offline-capable) composing of posts; the backend remains
  authoritative server-side at all times.
- **Web as an equivalent channel.** A web interface is available for login and for browsing
  larger galleries on a big screen.

### Positioning and base requirements

- **Global community.** The user base is worldwide, with a clear focus in the USA and
  further members in, among others, Japan and Europe. **Globally low latency** and
  **responsive** gallery browsing are base requirements.
- **Personal data.** User accounts and thus personal data are processed; careful handling of
  PII and the ability to delete an account are binding.

## Considered Options

- **Revive the legacy application / only cosmetically modernize it.**
  - *Pro:* lowest initial effort.
  - *Con:* remains web-centric, knows only single images, no store presence; misses the
    mobile recognition-and-story motive; the legacy stack is no longer operable.
- **Build marketplace / monetization immediately.**
  - *Pro:* faster commercial perspective.
  - *Con:* high complexity and acceptance risk before the user base lost since 2023 has even
    been regained; contradicts "value and reach first".
- **A free, mobile-first recognition community first (chosen).**
  - *Pro:* hits the core motive (recognition for husbandry work), minimizes the barrier to
    entry, and (re)builds the load-bearing user base.
  - *Con:* no revenue initially; requires discipline to keep the scope narrow.

## Rationale

The value of this community lies in **recognition for one's own husbandry work**. A free,
mobile-first app that puts the member's tank at the center and makes reputation visible hits
exactly this motive and lowers the barrier to win back the community scattered since 2023.
Monetization and a marketplace presuppose an active user base and are deliberately deferred.

## Consequences

- **Positive:** clear product focus on tanks and recognition; mobile store presence;
  revival of the existing content and community; a viable base for later stages.
- **Negative / Trade-offs:** no revenue in this stage; the operator initially bears the
  running costs themselves, which is why cost efficiency is a base requirement of the
  architecture; store compliance (e.g. account deletion) creates extra effort from the
  start.
- **To be decided later:** monetization, marketplace ("Trusted Profiles"), Q&A, and further
  gamification are **not** subject of this ADR and will be addressed in their own ADRs when
  needed.
