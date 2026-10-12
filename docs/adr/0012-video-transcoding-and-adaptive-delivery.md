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

3. **Self-hosted transcoding and delivery on Hetzner.** Video is transcoded with `ffmpeg`
   into HLS on the platform's own compute, stored in a separate S3-compatible bucket on Hetzner
   Object Storage and delivered from Hetzner. The platform depends on no external video service.
   A managed video provider (e.g. Bunny Stream, Mux) stays a later option: the app only knows HLS
   URLs behind a thin interface, and the originals stay in the platform's own storage, so a
   provider can take over by re-transcoding from them.

4. **Limits.** A video is at most 60 seconds long. The rendition ladder is 480p, 720p and
   1080p; higher-resolution originals are scaled down to 1080p and not kept. Audio is kept and
   can be removed at upload.

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
- **Managed video provider (Bunny Stream, Mux) from the start.**
  - *Pro:* nothing to build or operate; low per-GB prices.
  - *Con:* a dependency on an external service from day one, contrary to the platform's aim of
    running on its own infrastructure with swappable providers. Kept as a later option.
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
  - bitrates of the rendition ladder and the poster/thumbnail strategy,
  - accepted input codecs and the maximum upload file size,
  - caching close to the mostly US-based audience in front of the EU object storage,
  - whether **live** video is ever in scope (assumed VOD-only for now),
  - video-specific **moderation**.

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
