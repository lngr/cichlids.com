# ADR-0012: Video Transcoding and Adaptive Delivery

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

Video is a first-class medium alongside photos ([ADR-0001](0001-purpose-and-scope.md)).
Images are stored in R2 and delivered via the Cloudflare CDN
([ADR-0009](0009-hosting-and-global-delivery.md)). Video differs from images in ways that
matter for delivery: files are large and heterogeneous (codecs, resolutions, orientations), a
global audience needs **adaptive playback** (multiple bitrates), egress volume per item is far
higher, and moderation/abuse surfaces are different.

Crucially, serving large volumes of video over the general-purpose Cloudflare web CDN sits in
tension with that provider's policy on disproportionate non-HTML traffic — which would put the
**image delivery and the main delivery account at risk** (including the unexplained-suspension
risk that motivates the provider-portability stance in ADR-0009). It must be decided how
uploaded video becomes deliverable, adaptively streamed renditions without endangering the
image path.

## Decision

**Video is hosted and delivered on a separate channel, decoupled from the image stack.**
Specifically:

1. **Separation.** Video uses its **own storage and its own delivery path/domain**, distinct
   from the R2 + Cloudflare image path of ADR-0009. This insulates image delivery and the main
   Cloudflare account from video's policy, cost, and abuse profile, and lets video be swapped
   independently of images.

2. **Async transcode to adaptive renditions.** Uploaded video is transcoded
   **asynchronously**, triggered by the upload domain event
   ([ADR-0008](0008-event-driven-architecture.md)), into an **adaptive (HLS) rendition ladder**
   plus a poster image; renditions are **immutable** and CDN-delivered. A post carrying video
   becomes "ready" only once its renditions exist.

3. **Concrete video host/transcoder — open detail.** Two viable shapes, both keeping the
   portability spirit of ADR-0009 (prefer S3-compatible storage / swappable provider):
   - **A — self-hosted `ffmpeg` → HLS** into a separate S3-compatible bucket, fronted by a
     video-appropriate CDN. Full control, no per-minute fees; cost is transcoding compute
     (bursty, queue-driven).
   - **B — a dedicated managed video provider** (e.g. Bunny Stream, Mux, Cloudflare Stream).
     Simplest to operate and a natural fit now that video is separate; cost is per
     stored/delivered minute and some lock-in.

The **separation** (point 1) and the **async-adaptive direction** (point 2) are decided; the
concrete host/transcoder (point 3) is confirmed later.

## Considered Options

- **Separate video hosting/delivery channel (chosen).**
  - *Pro:* isolates video's distinct size/egress/policy/moderation profile; protects the image
    path and the main account from video-specific policy enforcement and ban risk; each medium
    is independently swappable; opens the door to a purpose-built video provider.
  - *Con:* a second delivery path and provider relationship to operate; cross-linking
    (post ↔ video) spans two systems.
- **Video on the same R2 + Cloudflare substrate as images.**
  - *Pro:* one storage and delivery path; reuses the zero-egress image setup.
  - *Con:* concentrates risk on one account and collides with the provider's
    disproportionate-non-HTML/video policy — a single enforcement action could take down image
    delivery too. Rejected for exactly this coupling.
- **Store and serve the original file as-is, no transcoding.**
  - *Con:* no adaptive playback; large originals are slow/costly globally; incompatible
    codecs/orientations break playback on many devices.
- **On-the-fly / per-request transcoding.**
  - *Con:* recurring per-request compute and poor cacheability; contradicts the
    immutable-objects model.

## Rationale

Video's profile — large files, high egress, provider policy on disproportionate non-HTML
traffic, distinct moderation — is different enough that mixing it into the image path would
endanger the very thing ADR-0009 protects: cheap, reliable image delivery on a swappable
provider. Hosting video separately isolates that risk and, as a bonus, makes a purpose-built
video provider a clean option. Producing renditions asynchronously off the upload event keeps
the write path fast and fits the event-driven architecture; immutable adaptive renditions cache
well wherever they are served.

## Consequences

- **Positive:** image delivery and the main account are insulated from video policy/cost/abuse;
  video is independently swappable; adaptive, globally fast playback; the upload path stays
  responsive.
- **Negative / Trade-offs:** a second delivery path and provider to operate and pay for;
  post↔video references span two systems; a transcoding pipeline (or a managed provider's
  per-minute cost) to run; storage grows with the rendition ladder.
- **To be decided later (detail):**
  - the **concrete video host/transcoder** (Option A vs B),
  - the **rendition ladder** (resolutions/bitrates) and poster/thumbnail strategy,
  - **limits** (max length, max file size) and accepted input codecs,
  - whether **live** video is ever in scope (assumed VOD-only for now),
  - video-specific **moderation**.

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
