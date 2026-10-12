"""Embeds all pictures of data/pictures.csv with one model and caches the result.

Cache layout: cache/<model>/emb.npy (float16, one row per csv row), state.npy
(0 pending, 1 done, 2 undecodable). Reruns resume where they stopped.
"""
import argparse
import csv
import os
import sys
import time

import numpy as np
import torch
from PIL import Image, ImageFile
from torch.utils.data import DataLoader, Dataset

Image.MAX_IMAGE_PIXELS = 200_000_000
ImageFile.LOAD_TRUNCATED_IMAGES = False

MODELS = {
    "bioclip": dict(kind="open_clip", name="hf-hub:imageomics/bioclip"),
    "bioclip2": dict(kind="open_clip", name="hf-hub:imageomics/bioclip-2"),
    "clip": dict(kind="open_clip", name="ViT-B-16", pretrained="laion2b_s34b_b88k"),
    "dinov2": dict(kind="hf", name="facebook/dinov2-base"),
}


def load_model(key):
    cfg = MODELS[key]
    if cfg["kind"] == "open_clip":
        import open_clip
        if "pretrained" in cfg:
            model, _, preprocess = open_clip.create_model_and_transforms(cfg["name"], pretrained=cfg["pretrained"])
        else:
            model, _, preprocess = open_clip.create_model_and_transforms(cfg["name"])
        model.eval()
        return model, preprocess, lambda x: model.encode_image(x)
    from transformers import AutoModel
    import torchvision.transforms as T
    model = AutoModel.from_pretrained(cfg["name"]).eval()
    preprocess = T.Compose([
        T.Resize(256, interpolation=T.InterpolationMode.BICUBIC), T.CenterCrop(224), T.ToTensor(),
        T.Normalize((0.485, 0.456, 0.406), (0.229, 0.224, 0.225))])
    return model, preprocess, lambda x: model(pixel_values=x).last_hidden_state[:, 0]


class Pics(Dataset):
    def __init__(self, paths, idx, preprocess):
        self.paths, self.idx, self.pre = paths, idx, preprocess

    def __len__(self):
        return len(self.idx)

    def __getitem__(self, i):
        row = self.idx[i]
        try:
            im = Image.open(self.paths[row])
            im.draft("RGB", (640, 640))
            im = im.convert("RGB")
            return row, self.pre(im).numpy()
        except Exception:
            return row, None


def collate(batch):
    ok = [(r, x) for r, x in batch if x is not None]
    bad = [r for r, x in batch if x is None]
    xs = np.stack([x for _, x in ok]) if ok else None
    return [r for r, _ in ok], xs, bad


def capped_rows(rows, cap, seed=1):
    """Boolean mask: at most cap randomly chosen rows per species (all rows when cap is 0)."""
    import random
    mask = [True] * len(rows)
    if cap <= 0:
        return mask
    by_species = {}
    for i, r in enumerate(rows):
        by_species.setdefault(r["species_id"], []).append(i)
    rng = random.Random(seed)
    for s in sorted(by_species):
        idx = by_species[s]
        rng.shuffle(idx)
        for i in idx[cap:]:
            mask[i] = False
    return mask


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, choices=sorted(MODELS))
    ap.add_argument("--csv", default="data/pictures.csv")
    ap.add_argument("--cache", default="cache")
    ap.add_argument("--threads", type=int, default=20)
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--batch", type=int, default=32)
    ap.add_argument("--cap", type=int, default=0,
                    help="embed at most this many photos per species (seeded random choice; 0 = all)")
    ap.add_argument("--limit", type=int, default=0, help="embed only the first N rows (smoke test)")
    args = ap.parse_args()

    with open(args.csv, newline="", encoding="utf-8", errors="surrogateescape") as f:
        rows = list(csv.DictReader(f))
    paths = [r["path"] for r in rows]
    in_cap = capped_rows(rows, args.cap)
    n = len(paths)
    if args.limit:
        n = min(n, args.limit)
    out = os.path.join(args.cache, args.model)
    os.makedirs(out, exist_ok=True)
    state_f, emb_f = os.path.join(out, "state.npy"), os.path.join(out, "emb.npy")
    torch.set_num_threads(args.threads)
    model, pre, enc = load_model(args.model)
    if os.path.exists(state_f):
        state = np.load(state_f)
        emb = np.load(emb_f, mmap_mode="r+")
    else:
        state = np.zeros(len(paths), dtype=np.uint8)
        emb = None
    todo = [i for i in range(n) if state[i] == 0 and in_cap[i]]
    print(f"{args.model}: {len(todo)} pending of {n}", flush=True)
    dl = DataLoader(Pics(paths, todo, pre), batch_size=args.batch, num_workers=args.workers, collate_fn=collate)
    t0, done, last_save = time.time(), 0, 0
    with torch.inference_mode():
        for rows, xs, bad in dl:
            for r in bad:
                state[r] = 2
            if xs is not None:
                f = enc(torch.from_numpy(xs)).float().numpy()
                if emb is None:
                    emb = np.lib.format.open_memmap(emb_f, mode="w+", dtype=np.float16, shape=(len(paths), f.shape[1]))
                emb[rows] = f.astype(np.float16)
                for r in rows:
                    state[r] = 1
            done += len(rows) + len(bad)
            if done - last_save >= 2000 or done == len(todo):
                if emb is not None:
                    emb.flush()
                np.save(state_f, state)
                last_save = done
                rate = done / (time.time() - t0)
                print(f"{args.model}: {done}/{len(todo)} ({rate:.1f} img/s, undecodable so far {(state == 2).sum()})", flush=True)
    if emb is not None:
        emb.flush()
    np.save(state_f, state)
    print(f"{args.model}: finished in {time.time() - t0:.0f}s, undecodable {(state == 2).sum()}", flush=True)


if __name__ == "__main__":
    main()
