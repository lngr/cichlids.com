# Scanner spike

Measures how well nearest-neighbour search over image embeddings (and small heads on frozen embeddings) recognizes
cichlid species on member photos of the legacy archive. The spike also prepares a comparison with a cheap cloud
vision model. Results are in RESULTS.md.

Everything runs on CPU. No archive image or archive data leaves the machine, except through the explicitly started
Gemini comparison described below. The legacy database is read with SELECT statements only.

## Layout

| Path | Purpose |
|---|---|
| prepare_data.py | Reads the legacy DB through the docker container and writes data/pictures.csv (one row per picture with exactly one species tag). |
| embed.py | Embeds pictures with one model and caches the vectors in cache/MODEL/. Resumable. |
| run_all.sh | Runs embed.py for all four models one after another. |
| evaluate.py | Member-disjoint split, kNN, zero-shot, linear probe, MLP head, group-of-3, genus fallback, intra-genus, hard cases. Writes data/results.json, data/scores.npz, data/split.npz. |
| render_tables.py | Prints the Markdown tables of data/results.json. |
| gemini_compare.py | Prepared comparison with a Gemini vision model on a fixed sample of test photos. |
| .gitignore | Ignores the venv, caches, data, logs and downloaded tools. Only code, README.md and RESULTS.md are tracked. |

## Setup

The machine has no pip and no venv module, so a standalone uv binary bootstraps the environment inside this
directory (ignored by git):

    mkdir -p .tools && cd .tools
    curl -fsSL -o uv.tgz https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-unknown-linux-gnu.tar.gz
    tar xzf uv.tgz && cd ..
    UV=$PWD/.tools/uv-x86_64-unknown-linux-gnu/uv
    $UV venv .venv --python 3.12
    $UV pip install --python .venv/bin/python torch torchvision --index-url https://download.pytorch.org/whl/cpu
    $UV pip install --python .venv/bin/python open_clip_torch transformers pillow numpy pandas scipy

On a machine with pip and venv, a plain `python -m venv .venv` with the same package list works as well.

## Rerun end to end

    .venv/bin/python prepare_data.py                 # data/pictures.csv, counts in the output
    CAP=200 ./run_all.sh                             # embeddings, about 5 hours with 20 torch threads
    .venv/bin/python evaluate.py                     # all metrics, data/results.json
    .venv/bin/python render_tables.py > data/tables.md

Notes on the embedding step:

- Caches live in cache/MODEL/ (emb.npy as float16 rows aligned to data/pictures.csv, state.npy with 0 pending,
  1 done, 2 undecodable). Model weights live in cache/hf (HF_HOME is set by run_all.sh). A rerun skips finished rows.
- CAP limits the photos per species (seeded random choice) so the run finishes in hours. CAP=0 embeds every photo
  of the candidate species (47,433 rows); rows already embedded are kept, so a larger cap only embeds the rest.
- Logs go to logs/embed_MODEL.log. Run long jobs in the background and poll the logs.
- DataLoader workers return numpy arrays because /dev/shm can be small (64 MB) in containers.

## Repeating the measurement with newer models

1. Add an entry to MODELS in embed.py (kind open_clip with a hub name, or kind hf for a transformers backbone whose
   first token output is the image embedding). Text-prompt zero-shot in evaluate.py applies to open_clip models listed
   in the tuple next to the zero-shot call.
2. `./run_all.sh NEWMODEL` embeds it with the same CAP as the earlier models (the split is derived from the rows that
   are embedded for all models, so use the same cap for all).
3. `.venv/bin/python evaluate.py --models bioclip,bioclip2,dinov2,clip,NEWMODEL` and render the tables.

Old caches stay valid as long as data/pictures.csv keeps its row order. A change of the database export requires
deleting cache/.

## Gemini comparison (prepared)

gemini_compare.py picks a fixed seeded sample of about 1,000 test photos (every selected species at least once,
the hard genera Tropheus, Maylandia, Metriaclima, Aulonocara, Pseudotropheus, Labidochromis and Neolamprologus
weighted three times), writes the picture ids to data/gemini_sample_ids.txt, and evaluates Gemini next to the
kNN, linear probe and MLP head on exactly that sample.

- Each image is decoded, scaled to at most 1024 px on the long side, rebuilt from raw pixels and saved as JPEG, so no
  EXIF, GPS, XMP or ICC data is sent. The request contains the pixels and a fixed prompt, nothing else.
- Two modes: constrained (the 3 most likely species from the list of selected species) and open (free scientific
  names, matched to the archive species by normalized name, with the epithet as synonym-insensitive fallback and
  Metriaclima treated as Maylandia). Output is forced to JSON with a species and a confidence.
- The key is read from the environment variable GEMINI_API_KEY or from ~/.config/cichlids/gemini.key. Without a key
  the script exits before any request. --dry-run and --evaluate-only never touch the network.
- Responses are cached per model, mode and picture id in cache/gemini/. --max-usd (default 10) stops the run when the
  reported token usage reaches the cap. Requests are paced by --rpm with exponential backoff on 429 and 5xx.

Commands:

    .venv/bin/python gemini_compare.py --dry-run
    GEMINI_API_KEY=... .venv/bin/python gemini_compare.py --model gemini-3.1-flash-lite --max-usd 10
    .venv/bin/python gemini_compare.py --evaluate-only

The request format follows the public REST documentation and is untested against the live API.
Run with --mode constrained --sample-size 10 first to confirm the response shape before the full run.
