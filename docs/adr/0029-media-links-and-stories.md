# ADR-0029: Media Links and Stories

- **Status:** Accepted
- **Date:** 2026-10-16
- **Deciders:** Alexander Langer

## Context and Problem Statement

A photo or video is stored once as a media item with its renditions
([ADR-0020](0020-relational-domain-schema.md), [ADR-0012](0012-video-transcoding-and-adaptive-delivery.md)).
The same file shows up in many places: in a section of a tank, on a care entry, as evidence of a
guided water change, in a gallery entry, in a forum post, as a profile picture. Planned features
add more places, such as an individual fish with a page of its own that members can follow.

Each place decides who may see what it shows. A tank can be public, visible to followers, to
chosen people or to its owner only; a care entry can be narrowed to its author; a published gallery
entry is public by intent. A file is visible to a viewer when at least one place that shows it is
visible to that viewer, and the storage area of the file (publicly readable or private behind
signed links) follows from whether anonymous viewers can see it. When every place answers this
question with its own table, its own rule and its own signed delivery route, the answers drift
apart, and each new place reopens the same security questions.

Gallery entries are stories in the sense of Instagram or imgur: an ordered sequence of photos and
videos, each with a caption that belongs to that story only. The same tank photo can appear in a
Christmas story and in the history story of the tank, with different texts, and stays one file. A
story can point to the tank it shows, to single inhabitants and to species, and such references
can appear as cards within the sequence. A story with a single photo is the common case, and every
picture of the predecessor platform is one. The gallery lists stories with a cover image the author
picks.

The question is how media attach to the places that show them, how stories are modeled on top of
that, and where the visibility of a file is decided.

## Decision

### One media item, one link table

- **The media item** is the only representation of a stored file. Besides storage key, kind,
  dimensions and checksum it records its provenance: its owner, whether the camera of the app took
  it, the capture time from the file and the time the server received it. Provenance belongs to
  the file and is the same in every place that shows it.
- **A media link** attaches one media item to one context. The contexts are tank, inhabitant,
  care entry, water change session, story, discussion post and profile. A link has a role within
  its context, a position among the links of the same context and role, and an optional caption
  that belongs to this link only. Examples of roles: showcase, decoration, technic, time-lapse and
  video for a tank; photo and video for a care entry; evidence for a water change session; item
  for a story; avatar and header for a profile; attachment for a discussion post.
- **The context reference** is a set of nullable foreign keys, one per context type, plus a
  context type column, with a check constraint that exactly the key matching the type is set. The
  database keeps referential integrity, and deleting a context removes its links.
- **Limits per context** (six photos per care entry, two videos, six evidence photos, one avatar)
  are rules of the context and are checked where links are created, with unique indexes wherever
  a limit is one.
- **Only the owner of a media item links it.** Linking a file into a context can widen who sees
  it, so a member can only attach files they own. Files without an owner (migrated forum
  attachments) are linked only by the import.
- **Every member has a media library** with all files they own. A file stays in the library when
  its last link goes away, and a fresh upload sits there until it is linked. Removing a link never
  removes the file. Only the owner deleting the file from the library removes it, together with its
  renditions and all of its links.

### Stories

- **A story** is the gallery entry: author, title, text, topic, draft or published state, the
  moderation trail, the interaction aggregates and the public addresses of the entry. Comments,
  ratings, collection entries and slug aliases refer to the story.
- **The items of a story** are its media links with role item. Each item has its own position and
  caption.
- **A story subject** points from a story to a tank, an inhabitant or a species. With a position
  it is a card within the sequence; without one it is a reference shown with the story. Items and
  subjects share one position sequence per story, which the application maintains within one
  transaction.
- **The cover** is one of the items of the story, referenced from the story. A story without an
  explicit cover shows its first item.
- **A published story is public.** Publishing is a deliberate disclosure by the owner of the files
  and does not depend on the visibility of the tank the files come from. A subject pointing to a
  tank or an inhabitant is shown only to viewers who may see that tank or inhabitant; other viewers
  see the story without that subject.
- **A single-photo story** is a story with one item. Every gallery entry is a story, whatever the
  number of its items.

### One visibility rule and one delivery path

- **Visibility of a link** follows from its context: a tank link from the profile visibility of
  the tank, a care entry or water change link from the history visibility of the tank and the
  visibility of the entry, an inhabitant link from the visibility of its tank, a story link from
  the state of the story (published: everyone; draft: its author), a profile link and a discussion
  post link from the state of that profile or post.
- **Visibility of a media item** for a viewer is the union of the visibility of its links. The
  owner always sees their own files; a file without links is visible to its owner only.
- **Storage placement** follows from the same rule: a file that anonymous viewers can see lives in
  the public area, every other file in the private area. Every change that can alter this answer
  (a link created or removed, a context changing its visibility, a story published or withdrawn)
  marks the media item for placement, and one placement worker moves files between the areas.
- **Private delivery** uses one signed route for photos and videos of every context. The API
  checks the visibility of the media item for the viewer when it issues a signed link; the route
  checks only the signature and its expiry.

## Considered Options

- **One link table per context.** Each place gets the columns it needs, and constraints are
  specific. Every new place needs its own table, its own visibility code and its own delivery
  route, and the union rule for a file shown in several places has to join all of them. Rejected.
- **Generic context type and context id without foreign keys.** One column pair covers every
  context, and new contexts need no schema change. The database can no longer guarantee that a
  link points to an existing context or remove links with their context; orphaned links would
  keep files visible. Rejected in favor of typed nullable keys, which follow the pattern for
  interaction targets ([ADR-0020](0020-relational-domain-schema.md)).
- **A story item table holding either a media item or a subject.** One position sequence with a
  unique index per story, at the cost of a second way to attach media that the visibility rule
  and the placement worker would have to know as well. Rejected.
- **Copying a file into each story.** Each story owns its copy, and captions and crops are
  independent. Storage grows with every reuse, the copies drift apart, and deleting a photo would
  leave copies behind. Rejected.
- **Removing files without links after a grace period.** Storage stays lean, and an accidental
  removal can be undone within the period. Members lose files they may want to reuse in a later
  story or tank, and reuse across contexts is the point of the link model. Rejected in favor of
  the media library.

## Rationale

A single link table puts the question "who sees this file" in one place. The placement worker and
the signed delivery route depend on that answer only, so a new context (such as an individual fish)
needs a context type, its roles and its visibility mapping, and inherits storage and delivery. Typed
nullable keys keep the integrity the database can give, which matters more here than avoiding a
schema change per context, because a link that outlives its context would leak a file. Stories fit
on top without a second media path: an item is a link with a caption, and a reused photo is a
second link to the same file.

## Consequences

- **Positive:** Media handling, provenance, visibility and delivery each exist once. Reusing a file
  across tanks, care entries and stories costs one row, and the media library offers every own file
  for reuse. New contexts such as individual inhabitants attach without new media tables.
- **Negative / Trade-offs:** Each context type adds a column to the link table and a branch to the
  visibility mapping. Storage grows with every file a member keeps in the library. The shared
  position sequence of items and subjects spans two tables and is an application invariant that
  tests must cover. Computing the visibility of a file shown in many places needs care to stay fast
  on lists.
- **Migration:** Existing attachments of every context become media links with their order,
  captured-in-app flags move to the media item, gallery entries become stories with their media as
  items, and their tank and species references become subjects. The legacy import writes the same
  shape ([ADR-0026](0026-legacy-import-runs-once-against-an-empty-target.md)).
- **To be decided later:** Individual inhabitants (name, identity, follow target) and the product
  rules for sharing a file into a story, such as how often one file may be shared and whether a
  quick share creates a single-photo story.

## References

- [ADR-0012: Video Transcoding and Adaptive Delivery](0012-video-transcoding-and-adaptive-delivery.md)
- [ADR-0020: Relational Domain Schema](0020-relational-domain-schema.md)
- [ADR-0026: Legacy Import Runs Once Against an Empty Target](0026-legacy-import-runs-once-against-an-empty-target.md)
