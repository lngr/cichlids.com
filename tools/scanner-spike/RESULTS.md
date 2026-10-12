# Scanner spike results

Run date: 2026-10-12. Hardware: 28 CPU cores shared with other workloads, 62 GB RAM, no GPU. Wall-clock time of the
full pipeline: about 2.7 hours of embedding (bioclip 28 min, dinov2 23 min, clip 19 min, bioclip2 91 min, measured
under varying load from other jobs) plus about 2 minutes for evaluation; including environment setup and development
the session took about 4 hours.

## Setup

- Models (CPU, torch 2.14 CPU wheels): BioCLIP (open_clip hf-hub imageomics/bioclip, ViT-B/16), BioCLIP 2
  (hf-hub imageomics/bioclip-2, ViT-L/14, available), DINOv2 ViT-B/14 (facebook/dinov2-base, class token), general CLIP
  baseline (open_clip ViT-B-16, laion2b_s34b_b88k). Images are decoded with PIL (JPEG draft mode), resized and
  center-cropped by each model's own preprocessing, embeddings are L2-normalized.
- kNN: cosine similarity over the reference split, top-k neighbours (k in 5, 10, 20), weight exp((sim - best sim) / 0.05),
  scores summed per species and normalized to 1. The reported confidence is that normalized score.
- Zero-shot: text prompt "a photo of GENUS species." for each of the 116 species, softmax over the 116 species.
- Linear probe and MLP head (one hidden layer of 512, dropout 0.2): trained on the frozen reference embeddings on CPU
  (AdamW, 60 epochs, cosine schedule, label smoothing 0.05, fixed hyperparameters, no tuning on the test split).
- Ensemble row: concatenation of the four L2-normalized embeddings, kNN on the result.

## Data counts

- Pictures with at least one species tag in the tagging table: 56,811 (8 duplicate tag rows collapsed). Pictures with
  several different species tags: 0, so all tagged pictures have exactly one species. Of these, 53,702 are neither
  deleted nor hidden; all of their files exist on disk.
- Species with at least 100 such photos: 116 (121 species with at least 90, which formed the embedding candidate pool
  of 47,433 photos).
- Embedding budget: at most 200 randomly chosen photos per species (seed 1), 21,270 photos embedded per model.
  5 of them are undecodable (BMP data or damaged files named .jpg, empty files) and are skipped, 21,265 remain. All 116
  species keep at least 100 decodable photos in this subset. The cap reduces the reference size to at most about 160
  photos per species. A reference built from the whole archive is larger, so the numbers below are probably a lower
  bound for the production setting (see caveats).
- Selected for the evaluation: 20,798 photos of 116 species in 54 genera from 1,609 members.
- Photo to tank mapping exists only for 64 of the selected photos (the tank table references picture ids in a
  free-text list), so groups are formed by member only.

## Split

Per species, whole members are moved to the test side (random member order, seed 7) until about 20% of the species'
photos are reached. A member never appears on both sides for the same species. Photos without a known member (stored
under the anonymous folder) go to the reference side only. Result: 16191 reference photos and 4607 test photos
(22% test). One species has fewer than 10 test photos: Amphilophus robertsoni (9). Every species has test photos.

## Per-model results (percent, test split of 4607 photos)

| Model | Method | Top-1 micro | Top-3 micro | Top-1 macro | Top-3 macro | Genus top-1 (species vote) | Genus top-1 (genus vote) |
|---|---|---|---|---|---|---|---|
| bioclip | kNN k=5 | 45.9 | 63.7 | 46.0 | 63.5 | 60.2 | 61.1 |
| bioclip | kNN k=10 | 47.6 | 67.7 | 47.9 | 67.4 | 61.3 | 62.3 |
| bioclip | kNN k=20 | 48.5 | 69.7 | 48.6 | 69.2 | 61.8 | 63.1 |
| bioclip | zero-shot text | 13.8 | 25.7 | 12.9 | 24.4 | 32.1 | 32.4 |
| bioclip | linear probe | 53.1 | 74.8 | 53.5 | 74.4 | 66.2 | 67.2 |
| bioclip | MLP head | 53.7 | 73.6 | 54.0 | 73.5 | 66.8 | 67.9 |
| bioclip2 | kNN k=5 | 55.5 | 72.6 | 56.4 | 72.8 | 71.2 | 71.5 |
| bioclip2 | kNN k=10 | 56.3 | 76.1 | 57.5 | 76.2 | 72.1 | 72.3 |
| bioclip2 | kNN k=20 | 56.2 | 78.1 | 57.3 | 78.2 | 72.1 | 72.0 |
| bioclip2 | zero-shot text | 14.5 | 28.0 | 13.9 | 27.2 | 27.6 | 29.7 |
| bioclip2 | linear probe | 62.6 | 83.6 | 63.4 | 83.6 | 77.1 | 78.0 |
| bioclip2 | MLP head | 62.9 | 82.7 | 63.8 | 82.7 | 76.6 | 77.4 |
| dinov2 | kNN k=5 | 45.0 | 62.3 | 45.7 | 62.6 | 61.5 | 62.4 |
| dinov2 | kNN k=10 | 46.9 | 66.0 | 47.6 | 66.1 | 63.5 | 63.9 |
| dinov2 | kNN k=20 | 47.4 | 68.3 | 47.9 | 68.1 | 63.6 | 63.5 |
| dinov2 | linear probe | 61.4 | 82.4 | 62.0 | 82.2 | 76.1 | 76.9 |
| dinov2 | MLP head | 61.7 | 80.9 | 62.2 | 80.9 | 76.4 | 77.4 |
| clip | kNN k=5 | 23.5 | 40.2 | 24.6 | 40.8 | 36.8 | 38.6 |
| clip | kNN k=10 | 26.1 | 43.5 | 27.2 | 44.2 | 39.1 | 41.0 |
| clip | kNN k=20 | 27.0 | 46.9 | 28.1 | 47.5 | 40.2 | 42.1 |
| clip | zero-shot text | 5.1 | 11.0 | 4.7 | 10.4 | 11.5 | 13.7 |
| clip | linear probe | 42.8 | 66.2 | 43.2 | 65.9 | 56.2 | 57.8 |
| clip | MLP head | 43.9 | 66.4 | 44.1 | 65.9 | 57.7 | 58.5 |
| ensemble | kNN k=5 | 52.7 | 70.2 | 53.6 | 70.6 | 68.5 | 69.5 |
| ensemble | kNN k=10 | 53.4 | 74.5 | 54.4 | 74.6 | 69.5 | 70.7 |
| ensemble | kNN k=20 | 54.2 | 76.4 | 55.1 | 76.6 | 69.9 | 70.9 |

Best kNN configuration: {'model': 'bioclip2', 'k': 20}; best linear probe model: bioclip2

Reading the table: micro averages over test photos, macro averages over species. Genus top-1 comes either from the
genus of the top-1 species or from summing the species scores per genus.

Findings: BioCLIP 2 is the best backbone (kNN k=20: 56.2% top-1, 78.1% top-3). A linear probe on its embeddings reaches
62.6% top-1 and 83.6% top-3, DINOv2 with a probe reaches 61.4% and 82.4%. With plain kNN the DINOv2 and BioCLIP
embeddings are about equal (47 to 49% top-1), the general CLIP baseline is far behind. Zero-shot with scientific names
is weak (14% top-1), so a reference set or a trained head is necessary. The MLP head adds nothing over the linear probe.
The concatenated ensemble does not beat BioCLIP 2 alone.

## Several images of one subject

No videos exist in the archive. Test photos of the same species by the same member are sorted by picture id and
chunked into groups of 3 (remainders dropped); the per-photo species scores of a group are averaged. Photos of one member
are often shots of the same fish in the same tank, which approximates frames of a short video but is not identical
to it (same tank is only known for 64 photos).

| Scorer | Groups | Photos | Species | Single top-1 | Single top-3 | Group top-1 | Group top-3 | Group genus top-1 |
|---|---|---|---|---|---|---|---|---|
| bioclip2 k=20 (best_single_model) | 1011 | 3033 | 115 | 56.6 | 79.8 | 69.1 | 91.3 | 82.3 |
| ensemble k=20 (ensemble) | 1011 | 3033 | 115 | 54.6 | 77.6 | 66.9 | 89.6 | 81.8 |

For the single-photo columns the same 3,033 photos are scored one by one, so both sides use identical photos.
Grouping lifts top-3 from 79.8% to 91.3% and top-1 from 56.6% to 69.1% for BioCLIP 2 (kNN k=20).

## Genus fallback

The scanner answers with the species list when the score margin (top-1 score minus top-2 score) reaches the threshold and
with the genus (genus of the highest summed genus score) otherwise. Best kNN configuration (BioCLIP 2, k=20).

| Margin threshold | Species answers (coverage) | Species top-1 when answered | Species top-3 when answered | Genus fallbacks | Genus accuracy of fallbacks | Overall correct (species or genus) |
|---|---|---|---|---|---|---|
| 0.0 | 100.0 | 56.2 | 78.1 | 0.0 | - | 56.2 |
| 0.1 | 68.6 | 69.9 | 87.8 | 31.4 | 49.0 | 63.3 |
| 0.2 | 52.1 | 77.6 | 91.9 | 47.9 | 54.3 | 66.4 |
| 0.3 | 41.9 | 82.7 | 94.0 | 58.1 | 58.2 | 68.5 |
| 0.4 | 34.3 | 87.5 | 95.7 | 65.7 | 60.5 | 69.8 |
| 0.5 | 27.2 | 90.4 | 96.7 | 72.8 | 63.1 | 70.5 |
| 0.6 | 22.1 | 93.1 | 97.2 | 77.9 | 64.8 | 71.0 |
| 0.7 | 17.3 | 95.5 | 98.0 | 82.7 | 66.5 | 71.5 |
| 0.8 | 13.3 | 96.9 | 98.5 | 86.7 | 67.9 | 71.8 |

A threshold of 0.2 answers with species for about half of the photos at 77.6% top-1 (91.9% top-3 in the shown
list) and names the genus for the rest at 54.3% genus accuracy. At 0.5 only 27% of the photos get a species, with 90.4% top-1.
The genus fallback is itself unreliable (49% to 68%) because low-margin photos are the difficult ones.

## Intra-genus difficulty

Test photos of one genus, scored only among the species of that genus (kNN restricted to reference photos of the genus,
probe probabilities restricted to the genus species). Only species with at least 100 photos take part, so the
species counts are small for some genera: Tropheus has 2 qualifying species (duboisi, moorii). The archive keeps
Maylandia (1 qualifying species, lombardoi) and Metriaclima (2: estherae, zebra) as separate genus entries; only
Metriaclima has two species and is shown, a merged Maylandia/Metriaclima group of 3 species is not computed.

| Genus | Species | Test photos | Chance top-1 | kNN top-1 | kNN top-3 | Probe top-1 | Probe top-3 |
|---|---|---|---|---|---|---|---|
| Tropheus | 2 | 82 | 50.0 | 89.0 | 100.0 | 92.7 | 100.0 |
| Metriaclima | 2 | 83 | 50.0 | 73.5 | 100.0 | 66.3 | 100.0 |
| Aulonocara | 14 | 603 | 7.1 | 35.3 | 69.0 | 41.0 | 76.9 |
| Pseudotropheus | 10 | 396 | 10.0 | 70.5 | 91.4 | 72.2 | 92.7 |
| Labidochromis | 3 | 119 | 33.3 | 97.5 | 100.0 | 96.6 | 100.0 |
| Neolamprologus | 4 | 132 | 25.0 | 93.2 | 99.2 | 92.4 | 100.0 |

Separating close relatives works for genera with few, visually distinct species (Labidochromis, Neolamprologus,
Tropheus with two species). Aulonocara with 14 species is hard: 35% top-1 for kNN and 41% for the probe against 7%
chance. Metriaclima zebra is also tagged as Pseudotropheus zebra in the archive (see hard cases), which
holds its Metriaclima accuracy down. Genera with many look-alike colour forms are the weak point.

## Hard cases

| Species A | Species B | Errors | Same genus |
|---|---|---|---|
| Cyphotilapia frontosa | Cyphotilapia gibberosa | 61 | yes |
| Aulonocara baenschi | Aulonocara maleri | 47 | yes |
| Symphysodon aequifasciatus | Symphysodon discus | 45 | yes |
| Aulonocara eureka | Aulonocara jacobfreibergi | 38 | yes |
| Herichthys carpintis | Herichthys cyanoguttatus | 37 | yes |
| Cichlasoma cyanoguttatum | Herichthys cyanoguttatus | 35 | no |
| Amphilophus citrinellus | Amphilophus labiatus | 33 | yes |
| Hemichromis bimaculatus | Hemichromis lifalili | 29 | yes |
| Archocentrus nigrofasciatus | Cichlasoma nigrofasciatum | 28 | no |
| Aulonocara rubescens | Aulonocara spec. "German Red" | 28 | yes |

| Species | Test photos | Top-1 | Top-3 |
|---|---|---|---|
| Aulonocara mamelela | 28 | 7.1 | 17.9 |
| Metriaclima zebra | 43 | 11.6 | 34.9 |
| Aulonocara stuartgrandi | 40 | 10.0 | 35.0 |
| Pseudotropheus zebra | 41 | 2.4 | 39.0 |
| Aulonocara jacobfreibergi | 47 | 14.9 | 44.7 |
| Labeotropheus trewavasae | 40 | 30.0 | 45.0 |
| Cichlasoma cyanoguttatum | 51 | 7.8 | 45.1 |
| Archocentrus spilurus | 22 | 40.9 | 45.5 |
| Labeotropheus fuelleborni | 47 | 36.2 | 48.9 |
| Aulonocara maylandi | 46 | 37.0 | 50.0 |

Several of the confused pairs are not recognition errors: Cichlasoma cyanoguttatum and Herichthys cyanoguttatus,
Archocentrus nigrofasciatus and Cichlasoma nigrofasciatum, Metriaclima zebra and Pseudotropheus zebra are the same
species under different names (synonym tags in the archive). The pair Cyphotilapia frontosa and Cyphotilapia gibberosa
(61 errors) is a recognizable split of look-alike geographic forms. Aulonocara fills four of the ten worst species.

Best species by top-3 (100% each): Nandopsis tetracanthus, Satanoperca jurupari, Thorichthys meeki, Uaru amphiacanthoides,
Vieja argentea.

## Linear probe versus kNN

The probe improves on kNN for every backbone by 4 to 14 points top-1, most for DINOv2 (47.4% to 61.4%). It needs a short
training run on CPU after every change of the reference set, kNN does not.

## What a GPU fine-tune would need

Full backbone fine-tuning was out of scope on this machine. An estimate for a later attempt: a single GPU with 24 GB
(ViT-B models) or 40 GB (BioCLIP 2 ViT-L/14), about 20,000 to 50,000 training photos at 224 px, 10 to 20 epochs with
mixed precision, low learning rate (about 1e-5 with layer-wise decay), augmentation (crop, flip, colour jitter), and
the same member-disjoint split for validation. That is roughly one to a few GPU hours per model. The estimate comes
from typical ViT fine-tuning budgets and was not measured here.

## Cloud vision model cost (estimate, no calls made)

Prices read on 2026-10-12 from the official pages below. Assumed request: one 1024 px image, about 200 text input
tokens, 300 output tokens. Image token counts follow each vendor's published formula for a 1024 x 1024 image
(Gemini: 4 tiles of 258 = 1,032; Claude: ceil(1024/28)^2 = 1,369; gpt-5-nano 1,536 and gpt-5-mini 1,229 and gpt-4.1-mini
1,659 from 1,024 patches times the model multiplier). Mistral does not publish a figure on the pages read, so a range of
1,400 to 4,100 image tokens is assumed (4,100 corresponds to 16 px patches without merging). Models that reason or
think can add hidden output tokens, which would raise the output side.

| Model | Input USD per 1M | Output USD per 1M | Input tokens | Cost per call (USD) | Cost per 1,000 calls (USD) |
|---|---|---|---|---|---|
| Gemini 2.5 Flash-Lite (access limited to existing users) | 0.10 | 0.40 | 1,232 | 0.00024 | 0.24 |
| Gemini 3.1 Flash-Lite | 0.25 | 1.50 | 1,232 | 0.00076 | 0.76 |
| Gemini 3.5 Flash-Lite | 0.30 | 2.50 | 1,232 | 0.00112 | 1.12 |
| Gemini 2.5 Flash | 0.30 | 2.50 | 1,232 | 0.00112 | 1.12 |
| Claude Haiku 5.5 | 0.10 | 0.50 | 1,569 | 0.00031 | 0.31 |
| Claude Haiku 4.5 | 1.00 | 5.00 | 1,569 | 0.00307 | 3.07 |
| OpenAI gpt-5-nano | 0.05 | 0.40 | 1,736 | 0.00021 | 0.21 |
| OpenAI gpt-5-mini | 0.25 | 2.00 | 1,429 | 0.00096 | 0.96 |
| OpenAI gpt-4.1-mini | 0.40 | 1.60 | 1,859 | 0.00122 | 1.22 |
| Mistral Ministral 3 8B | 0.15 | 0.15 | 1,600 to 4,300 | 0.00029 to 0.00069 | 0.29 to 0.69 |
| Mistral Small 4 | 0.15 | 0.60 | 1,600 to 4,300 | 0.00042 to 0.00083 | 0.42 to 0.83 |

All candidates cost between roughly 0.2 and 1.2 USD per 1,000 calls (Claude Haiku 4.5 3.1 USD), so even 100,000 optional
tip calls stay in the low hundreds of USD. Sources:

- https://ai.google.dev/gemini-api/docs/pricing and https://ai.google.dev/gemini-api/docs/image-understanding
- https://platform.claude.com/docs/en/about-claude/pricing and https://platform.claude.com/docs/en/build-with-claude/vision
- https://developers.openai.com/api/docs/pricing and https://developers.openai.com/api/docs/guides/images-vision
  (the page https://openai.com/api/pricing/ returned HTTP 403)
- https://docs.mistral.ai/inference/pricing

## Gemini comparison (pending API key)

gemini_compare.py is prepared and has not sent any request. Sample: 1,000 test photos, all 116 species represented,
hard genera oversampled (Tropheus 27, Maylandia 8, Metriaclima 33, Aulonocara 206, Pseudotropheus 142, Labidochromis 55,
Neolamprologus 46). Default model gemini-3.1-flash-lite: per the Gemini model and deprecation pages read on 2026-10-12
it is the cheapest stable Flash-Lite model open to new projects (gemini-2.5-flash-lite is cheaper but limited to
existing users; gemini-3.5-flash-lite is the recommended successor at a higher price). Dry-run output:

```
sample: 1000 test photos, 116 species, hard genera Tropheus=27, Maylandia=8, Metriaclima=33, Aulonocara=206, Pseudotropheus=142, Labidochromis=55, Neolamprologus=46
mode constrained: 1000 requests, about 1757 input and 150 output tokens each
mode open: 1000 requests, about 1112 input and 150 output tokens each
model gemini-3.1-flash-lite, price 0.25/1.5 USD per 1M tokens (in/out)
total: 2000 requests, 2869000 input tokens, 300000 output tokens, estimated cost 1.17 USD (cap 10.0 USD)
no network calls were made
```

Command to run once the key exists (set GEMINI_API_KEY or create ~/.config/cichlids/gemini.key):

    .venv/bin/python gemini_compare.py --model gemini-3.1-flash-lite --max-usd 10
    .venv/bin/python render_tables.py   # unchanged; Gemini results are in data/gemini_results_MODEL.json

The script prints top-1, top-3 and genus accuracy for Gemini (constrained and open) next to kNN, linear probe and MLP
head restricted to the same 1,000 photos, and stores per-genus numbers for the hard genera in the JSON file. For
Gemini the per-genus numbers are unrestricted (the answer may come from any genus), for kNN and probe the JSON also
holds restricted intra-genus numbers. The sample file is data/gemini_sample_ids.txt.

## Caveats

- Labels are member-set tags. Colour morphs, geographic forms and hybrids blur species boundaries, and members tag with
  inconsistent names. The archive contains synonym pairs as separate species (examples above), which lowers measured
  accuracy; merging synonyms would raise it.
- The evaluation uses only the 116 frequent species. A production scanner sees photos of rare species and non-fish photos
  as well, which no method here is tested against.
- At most 200 photos per species were embedded, so reference sets hold up to about 160 photos per species. Larger
  references typically help kNN; the full-archive number is not measured.
- Split by member per species: a member's photos of species A can be in the test set while the same member's photos of
  species B are in the reference set (same tank and background, different label). Groups of 3 use photos of one member
  and species, which are often near-duplicates of one animal; the gain over single photos is therefore an upper
  estimate for real video frames.
- Photos are center-cropped to the model's input size, which can cut off elongated fish.
- A linear probe and MLP head use fixed hyperparameters and were not tuned; their numbers could move by a few points.
- The test split holds about 22% of the photos and is a single random draw (seed 7); no confidence intervals were computed.
- The Gemini request format follows the public documentation and is untested against the live API.
