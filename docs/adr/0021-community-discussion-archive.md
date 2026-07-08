# ADR-0021: Community Discussion Archive

- **Status:** Proposed
- **Date:** 2026-07-08
- **Deciders:** Alexander Langer

## Context and Problem Statement

The predecessor platform ran a discussion forum whose content (tens of thousands of
posts across two decades) is preserved in the rescued database. The threads are
long-tail search content about species identification and husbandry, and external
sites link to the forum URLs. The forum software and its account system are not part
of the new platform ([ADR-0002](0002-technology-stack.md)); forum credentials cannot
migrate to the new identity provider.

The platform vision includes a community question-and-answer section. The question is
how the preserved forum content relates to it.

## Decision

Forum content migrates into **first-class discussion entities of the new platform**
(threads and posts) rather than staying in a resurrected forum installation.

- **Archived state.** Migrated threads and posts are read-only archive content,
  rendered and indexable, clearly presented as archive. New discussions are written
  through the platform with its own identity ([ADR-0002](0002-technology-stack.md))
  once the community section opens for writing; the archive and new discussions share
  the same entities.
- **Authorship mapping.** A forum author whose e-mail address matches a migrated
  member profile is attributed to that profile. Other registered forum authors map to
  unlisted placeholder profiles. Guest posts keep their display name without a profile.
- **Privacy.** E-mail addresses stored alongside forum posts are never exposed.
  Private messages and forum credentials are not migrated.
- **Attachments** become media items in the object store
  ([ADR-0009](0009-hosting-and-global-delivery.md)).
- **Legacy URLs** of the forum redirect permanently to the new thread routes; requests
  for content that did not migrate answer with a gone status.

## Considered Options

- **Migrate into platform entities (chosen).** One content model, one search index,
  one rendering stack; the community section grows out of the archive.
- **Resurrect the forum software read-only.** Fastest visually, but a second stack
  (PHP, its own templates, its own security surface) for frozen content, and no path
  to the integrated community section. Rejected.
- **Static HTML export.** Cheap to serve, but content leaves the domain model, cannot
  be searched or linked to profiles, and a later community section would start from
  zero. Rejected.
- **Do not migrate.** Loses substantial search relevance and community memory.
  Rejected.

## Consequences

- The domain schema gains discussion entities; the ETL gains a forum stage with
  e-mail matching, placeholder authorship and attachment export.
- The archive presentation must make the read-only nature and the age of the content
  visible to avoid confusing it with live discussions.
- Redirect handling for the legacy forum URL scheme becomes part of the platform
  routing.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0020: Relational Domain Schema](0020-relational-domain-schema.md)
