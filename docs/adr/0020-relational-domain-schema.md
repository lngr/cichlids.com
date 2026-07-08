# ADR-0020: Relational Domain Schema

- **Status:** Accepted
- **Date:** 2026-07-08
- **Deciders:** Alexander Langer

## Context and Problem Statement

The application is rebuilt around the tank as the central entity
([ADR-0001](0001-purpose-and-scope.md)) on PostgreSQL ([ADR-0002](0002-technology-stack.md)).
The data of the predecessor platform (photos, tanks, comments, ratings, species taxonomy,
users) is migrated into the new schema through a repeatable, idempotent ETL. The legacy
schema is the source of the data, not the template for the new model.

Requirements shaping the schema:

1. **Legacy parity plus forward fit.** Every migrated entity must be representable, and
   the model must host the planned forward features (stories with multiple media,
   video media, collections, follows, a care/tracking event stream) as additive
   extensions rather than restructurings.
2. **Idempotent migration.** Every migrated row must be traceable to its legacy key so
   that ETL runs can repeat without duplicating data.
3. **Event seam.** Domain changes must be able to record an outbox event in the same
   transaction ([ADR-0008](0008-event-driven-architecture.md)).
4. **Provider-decoupled media.** Media rows reference storage keys behind an S3-style
   abstraction ([ADR-0009](0009-hosting-and-global-delivery.md)); URLs are derived, never
   stored as absolute links to a provider.

## Decision

### Entity catalog

| Area | Tables | Purpose |
|---|---|---|
| Identity | `profile`, `profile_identity` | Public member profile; external identity subjects (OIDC subs, legacy identities) linked n:1 to a profile for login mapping and IdP migration. |
| Taxonomy | `species`, `species_common_name`, `species_link` | Species catalog with genus, care ranges (temperature, ph, gh, kh), size, breeding, aggression and diet classifications, common names, and reference links. |
| Tanks | `tank`, `inhabitant`, `tank_media` | Tank with owner, biotope category, dimensions, equipment and setup texts, water values and feeding notes; stocking as inhabitant rows (species reference plus count); media attached to tank sections (showcase, decoration, technic) with explicit ordering. |
| Media | `media_item`, `media_variant` | Stored file (photo or video) with owner, storage key of the original, checksum and dimensions; generated renditions as variant rows with label, width and storage key. |
| Posts | `post`, `post_media` | Publication unit (single post or multi-media story) with author, optional tank link, topic, title, description, draft/published state, moderation trail, view counter and denormalized rating aggregate; ordered media through `post_media`. |
| Interaction | `comment`, `rating`, `comment_vote` | Comment (text) and rating (stars) as separate entities, each targeting exactly one post or one tank; up/down votes on comments with one vote per member and comment. |
| Curation | `collection`, `collection_entry`, `follow` | Member-curated ordered sets of posts; follow relations from a profile to a tank or another profile. |
| Addressing | `slug_alias` | Public URL aliases per post: every alias stays resolvable, exactly one is canonical per post. |
| Events | `outbox_event`, `webhook_subscription` | Transactional outbox rows and registered webhook consumers ([ADR-0008](0008-event-driven-architecture.md)). |

### Key modeling choices

1. **Post and media are separate entities.** A `media_item` is a stored file; a `post`
   is the act of publishing one or more media items. This carries the story format and
   video ([ADR-0012](0012-video-transcoding-and-adaptive-delivery.md)) without schema
   changes. Profile pictures, avatars and tank section images are media items without a
   post; they attach to `profile` and `tank_media` directly.
2. **Comment and rating are separate entities.** The legacy platform stored a star
   rating inside its comment rows. In the new model a rating is its own row; the ETL
   splits legacy rows into a comment row (non-empty text), a rating row (stars given),
   or both. Aggregates on the target are derived values.
3. **Polymorphic interaction targets via paired nullable references.** `comment` and
   `rating` have nullable `post_id` and `tank_id` with a check constraint that exactly
   one is set. This keeps referential integrity in the database without a table per
   target type.
4. **Bigint identity keys plus alias addressing.** Internal keys are `bigint` identity
   columns. Public URLs address posts through `slug_alias`, which preserves every legacy
   alias and marks one canonical alias per post; new posts mint a generated short slug.
   Numeric ids are not part of public URLs for posts.
5. **`legacy_id` bridge on every migrated table.** Each table whose rows originate from
   the legacy database has a nullable `legacy_id` with a unique index. The ETL upserts
   on it; rows created in the new application leave it null.
6. **Enumerations as text with check constraints**, mapped to C# enums by name. This
   keeps migrations additive (extend the constraint) and the data self-describing in
   SQL tooling, at the cost of a few bytes per row over integer codes.
7. **Timestamps as `timestamptz`.** Legacy Unix integer timestamps are converted during
   ETL. Creation time and publication time are distinct columns because drafts publish
   later than they are created.
8. **Derived aggregates are recomputed, never authored.** View counters, rating averages
   and counts, and comment scores live as denormalized columns for read performance, and
   the application owns their recomputation from the base tables. The base tables are
   the source of truth.
9. **Moderation as a trail, not a boolean.** Content removal keeps the row and records
   removal time, reason and acting profile; public queries filter on state. This
   preserves the moderation history of the migrated dataset and supports the same
   pattern for new content.

### Care/tracking extension path

The care layer (readings, maintenance events, milestones) is not part of this schema.
It attaches later as an event stream referencing `tank.id` and `inhabitant.id`; both
therefore have stable primary keys from the start, and nothing in this schema needs to
change for that extension.

## Considered Options

- **One generic content table for pictures, posts and attachments.** Fewer tables, but
  every consumer must branch on a type discriminator, constraints weaken (a profile
  avatar could reference a story), and the legacy pid overloading would effectively be
  reproduced. Rejected.
- **Combined comment-with-rating rows (legacy shape).** Simplest ETL, but pure ratings
  would be comments with empty text, and rating semantics could never evolve
  independently (one rating per member per target, rating without text in clients).
  Rejected in favor of the split with a shared legacy key.
- **UUID primary keys.** Provider-neutral and mergeable, but larger indexes across the
  largest tables (millions of comment rows) with no distributed-write requirement, and
  public addressing is already covered by slugs. Rejected.
- **Native PostgreSQL enum types.** Slightly smaller storage, but altering them is more
  ceremony than extending a check constraint, and provider-side enum churn during the
  early schema phase outweighs the benefit. Rejected.

## Consequences

- The ETL maps legacy rows onto a clean target model and records every skip or split
  decision against the `legacy_id` bridge, so migration runs are repeatable and
  auditable.
- Forward features (stories, video, collections, follows, care events) extend the
  schema additively.
- Derived aggregates need explicit recomputation paths in the application; stale
  aggregates are a known failure mode to test for.
- The paired-nullable-reference pattern for interaction targets must be enforced with
  check constraints and covered by tests, because the type system alone does not
  prevent a row with both references set.

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0012: Video Transcoding and Adaptive Delivery](0012-video-transcoding-and-adaptive-delivery.md)
- [ADR-0019: Persistence and Migration Tooling](0019-persistence-and-migration-tooling.md)
