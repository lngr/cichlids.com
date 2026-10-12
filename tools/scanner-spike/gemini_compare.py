"""Compares a cheap Gemini vision model with the embedding approaches on one fixed test sample.

Only re-encoded pixels leave the machine (JPEG, long side at most 1024 px, no metadata). Requests need an API key
(env GEMINI_API_KEY or ~/.config/cichlids/gemini.key). --dry-run and --evaluate-only make no network calls.
"""
import argparse
import base64
import csv
import io
import json
import os
import random
import re
import sys
import time
import urllib.error
import urllib.request
from collections import defaultdict

import numpy as np
from PIL import Image

HARD_GENERA = ("Tropheus", "Maylandia", "Metriaclima", "Aulonocara", "Pseudotropheus", "Labidochromis", "Neolamprologus")
GENUS_ALIAS = {"metriaclima": "maylandia"}
DEFAULT_MODEL = "gemini-3.1-flash-lite"
# USD per 1M tokens (input, output) from https://ai.google.dev/gemini-api/docs/pricing, read 2026-10-12
PRICES = {
    "gemini-3.1-flash-lite": (0.25, 1.50),
    "gemini-3.5-flash-lite": (0.30, 2.50),
    "gemini-2.5-flash-lite": (0.10, 0.40),
    "gemini-2.5-flash": (0.30, 2.50),
}
IMAGE_TOKENS = 1032      # 1024 x 1024 px: 4 tiles of 258 tokens per the image understanding docs
EST_OUT_TOKENS = 150
ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"

SCHEMA = {"type": "OBJECT", "properties": {"predictions": {"type": "ARRAY", "items": {
    "type": "OBJECT", "properties": {"species": {"type": "STRING"}, "confidence": {"type": "NUMBER"}},
    "required": ["species", "confidence"]}}}, "required": ["predictions"]}


def prompt_text(mode, species):
    base = ("This is a photo of one aquarium cichlid fish. Name the 3 most likely species, most likely first, "
            "with the scientific name (genus and species) and a confidence between 0 and 1. ")
    if mode == "constrained":
        return base + "Choose only from this list:\n" + "\n".join(species)
    return base + "Answer freely with the scientific names you consider most likely."


def load_key():
    key = os.environ.get("GEMINI_API_KEY", "").strip()
    if key:
        return key
    p = os.path.expanduser("~/.config/cichlids/gemini.key")
    if os.path.isfile(p):
        return open(p).read().strip()
    return ""


def select_sample(test_idx, y_test, genus_of, genus_names, sp_count, n, seed, oversample=3.0):
    """All species once, then weighted draw (hard genera weigh more). Deterministic for a seed."""
    rng = random.Random(seed)
    hard = {genus_names.index(g) for g in HARD_GENERA if g in genus_names}
    chosen = []
    by_sp = defaultdict(list)
    for j, c in enumerate(y_test):
        by_sp[int(c)].append(j)
    for c in sorted(by_sp):
        chosen.append(rng.choice(by_sp[c]))
    taken = set(chosen)
    keyed = []
    for j, c in enumerate(y_test):
        if j in taken:
            continue
        w = oversample if int(genus_of[c]) in hard else 1.0
        keyed.append((rng.random() ** (1.0 / w), j))
    keyed.sort(reverse=True)
    chosen += [j for _, j in keyed[:max(0, n - len(chosen))]]
    return sorted(chosen)


def encode_clean_jpeg(path):
    """Pixels only: a fresh image is built from the raw pixel bytes so no metadata can carry over."""
    im = Image.open(path)
    im.draft("RGB", (2048, 2048))
    im = im.convert("RGB")
    im.thumbnail((1024, 1024), Image.LANCZOS)
    clean = Image.frombytes("RGB", im.size, im.tobytes())
    buf = io.BytesIO()
    clean.save(buf, "JPEG", quality=88, exif=b"", icc_profile=None)
    return buf.getvalue()


def norm(s):
    return re.sub(r"[^a-z ]", "", s.lower().replace("_", " ")).strip()


class Matcher:
    def __init__(self, species, genus_names):
        self.full = {norm(s): i for i, s in enumerate(species)}
        self.epithet = defaultdict(list)
        for i, s in enumerate(species):
            self.epithet[norm(s).split()[-1]].append(i)
        self.genera = {norm(g) for g in genus_names}

    def species(self, name):
        n = norm(name)
        parts = n.split()
        if len(parts) >= 2:
            parts[0] = GENUS_ALIAS.get(parts[0], parts[0])
            n2 = " ".join(parts[:2])
            for cand in (n, n2):
                if cand in self.full:
                    return self.full[cand]
                alt = " ".join([parts[0]] + parts[1:2])
                if alt in self.full:
                    return self.full[alt]
            ep = self.epithet.get(parts[1], [])
            # synonym-insensitive: the epithet alone decides when exactly one of our species has it
            if len(ep) == 1:
                return ep[0]
            # genus synonyms: Maylandia and Pseudotropheus share epithets, so try the alias pair too
            for g in ("maylandia", "pseudotropheus", "metriaclima"):
                k = f"{GENUS_ALIAS.get(g, g)} {parts[1]}"
                if k in self.full:
                    return self.full[k]
        return -1


def ranked_from_scores(scores):
    return np.argsort(-scores, 1)[:, :3]


def rank_metrics(ranked, y, genus_of, genus_pred=None):
    ranked = np.asarray(ranked)
    hit1 = ranked[:, 0] == y
    hit3 = (ranked == y[:, None]).any(1)
    gp = genus_of[np.where(ranked[:, 0] >= 0, ranked[:, 0], 0)] if genus_pred is None else genus_pred
    gt = genus_of[y]
    return dict(n=int(len(y)), top1=float(hit1.mean()), top3=float(hit3.mean()), genus_top1=float((gp == gt).mean()))


def per_genus(ranked, y, genus_of, genus_names, restricted_scores=None):
    out = {}
    for g in HARD_GENERA:
        if g not in genus_names:
            continue
        gi = genus_names.index(g)
        m = genus_of[y] == gi
        if not m.any():
            continue
        row = dict(n=int(m.sum()), top1=float((ranked[m, 0] == y[m]).mean()),
                   top3=float((ranked[m] == y[m][:, None]).any(1).mean()))
        if restricted_scores is not None:
            sps = np.where(genus_of == gi)[0]
            sub = restricted_scores[m][:, sps]
            order = np.argsort(-sub, 1)
            yy = np.array([list(sps).index(c) for c in y[m]])
            row["restricted_top1"] = float((order[:, 0] == yy).mean())
            row["restricted_top3"] = float((order[:, :3] == yy[:, None]).any(1).mean())
        out[g] = row
    return out


def call_api(model, key, text, jpeg, mode):
    body = {"contents": [{"parts": [{"text": text}, {"inline_data": {"mime_type": "image/jpeg",
                                                                    "data": base64.b64encode(jpeg).decode()}}]}],
            "generationConfig": {"temperature": 0, "responseMimeType": "application/json",
                                 "responseSchema": SCHEMA, "maxOutputTokens": 800}}
    req = urllib.request.Request(ENDPOINT.format(model=model), data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    delay = 5.0
    for attempt in range(7):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504) and attempt < 6:
                time.sleep(delay)
                delay = min(delay * 2, 120)
                continue
            raise RuntimeError(f"HTTP {e.code}") from None
        except (urllib.error.URLError, TimeoutError):
            if attempt < 6:
                time.sleep(delay)
                delay = min(delay * 2, 120)
                continue
            raise


def parse_response(resp):
    try:
        text = resp["candidates"][0]["content"]["parts"][0]["text"]
        preds = json.loads(text)["predictions"]
        return [(str(p["species"]), float(p.get("confidence", 0))) for p in preds][:3]
    except Exception:
        return []


def usage_cost(resp, price):
    u = resp.get("usageMetadata", {})
    t_in = u.get("promptTokenCount", 0)
    t_out = u.get("candidatesTokenCount", 0) + u.get("thoughtsTokenCount", 0)
    return (t_in * price[0] + t_out * price[1]) / 1e6


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--cache", default="cache")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--mode", default="both", choices=["constrained", "open", "both"])
    ap.add_argument("--sample-size", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--max-usd", type=float, default=10.0)
    ap.add_argument("--rpm", type=float, default=60.0)
    ap.add_argument("--price-in", type=float, help="USD per 1M input tokens (overrides the built-in table)")
    ap.add_argument("--price-out", type=float, help="USD per 1M output tokens")
    ap.add_argument("--dry-run", action="store_true", help="no network: print request count, tokens and cost estimate")
    ap.add_argument("--evaluate-only", action="store_true", help="no network: score whatever is cached")
    args = ap.parse_args()

    price = PRICES.get(args.model)
    if args.price_in is not None and args.price_out is not None:
        price = (args.price_in, args.price_out)
    if price is None:
        sys.exit(f"no price known for {args.model}; pass --price-in and --price-out")

    sp = np.load(os.path.join(args.data, "split.npz"))
    names = json.load(open(os.path.join(args.data, "split_names.json")))
    species, genus_names = names["species"], names["genera"]
    test_idx, y_test, genus_of = sp["test_idx"], sp["y_test"], sp["genus_of"]
    with open(os.path.join(args.data, "pictures.csv"), newline="", encoding="utf-8", errors="surrogateescape") as f:
        rows = list(csv.DictReader(f))
    sample = select_sample(test_idx, y_test, genus_of, genus_names, len(species), args.sample_size, args.seed)
    ids = [int(rows[test_idx[j]]["pic"]) for j in sample]
    with open(os.path.join(args.data, "gemini_sample_ids.txt"), "w") as f:
        f.write("\n".join(map(str, ids)) + "\n")
    modes = ["constrained", "open"] if args.mode == "both" else [args.mode]
    sample_genus = defaultdict(int)
    for j in sample:
        sample_genus[genus_names[genus_of[y_test[j]]]] += 1
    print(f"sample: {len(sample)} test photos, {len(set(y_test[sample].tolist()))} species, hard genera "
          + ", ".join(f"{g}={sample_genus[g]}" for g in HARD_GENERA if g in sample_genus))

    cdir = {m: os.path.join(args.cache, "gemini", args.model, m) for m in modes}
    for d in cdir.values():
        os.makedirs(d, exist_ok=True)
    texts = {m: prompt_text(m, species) for m in modes}

    if args.dry_run:
        total_in = total_out = 0
        reqs = 0
        for m in modes:
            pending = sum(1 for i in ids if not os.path.exists(os.path.join(cdir[m], f"{i}.json")))
            text_tokens = len(texts[m]) // 4 + 20
            reqs += pending
            total_in += pending * (IMAGE_TOKENS + text_tokens)
            total_out += pending * EST_OUT_TOKENS
            print(f"mode {m}: {pending} requests, about {IMAGE_TOKENS + text_tokens} input and {EST_OUT_TOKENS} output tokens each")
        cost = (total_in * price[0] + total_out * price[1]) / 1e6
        print(f"model {args.model}, price {price[0]}/{price[1]} USD per 1M tokens (in/out)")
        print(f"total: {reqs} requests, {total_in} input tokens, {total_out} output tokens, estimated cost {cost:.2f} USD "
              f"(cap {args.max_usd} USD)")
        print("no network calls were made")
        return

    spent = 0.0
    for m in modes:
        for i in ids:
            p = os.path.join(cdir[m], f"{i}.json")
            if os.path.exists(p):
                spent += usage_cost(json.load(open(p)), price)
    if not args.evaluate_only:
        key = load_key()
        if not key:
            sys.exit("no API key: set GEMINI_API_KEY or create ~/.config/cichlids/gemini.key")
        pic_path = {int(r["pic"]): r["path"] for r in rows}
        interval = 60.0 / args.rpm
        stop = False
        for m in modes:
            for n_done, i in enumerate(ids):
                p = os.path.join(cdir[m], f"{i}.json")
                if os.path.exists(p):
                    continue
                if spent >= args.max_usd:
                    print(f"cost cap {args.max_usd} USD reached ({spent:.2f}), stopping")
                    stop = True
                    break
                t0 = time.time()
                try:
                    resp = call_api(args.model, key, texts[m], encode_clean_jpeg(pic_path[i]), m)
                except Exception as e:
                    print(f"{m} {i}: {type(e).__name__} {e}", flush=True)
                    continue
                with open(p, "w") as f:
                    json.dump(resp, f)
                spent += usage_cost(resp, price)
                if n_done % 25 == 0:
                    print(f"{m}: {n_done}/{len(ids)}, spent {spent:.3f} USD", flush=True)
                time.sleep(max(0.0, interval - (time.time() - t0)))
            if stop:
                break
    print(f"spent according to cached usage: {spent:.3f} USD")

    # evaluation on the sample
    ys = y_test[sample]
    matcher = Matcher(species, genus_names)
    result = {"model": args.model, "sample": len(sample), "spent_usd": spent, "methods": {}}
    res_json = os.path.join(args.data, "results.json")
    scores = np.load(os.path.join(args.data, "scores.npz"))
    for key in scores.files:
        sc = scores[key][sample].astype(np.float32)
        r = ranked_from_scores(sc)
        result["methods"][key] = dict(overall=rank_metrics(r, ys, genus_of), per_genus=per_genus(r, ys, genus_of, genus_names, sc))
    for m in modes:
        ranked = np.full((len(sample), 3), -1)
        gpred = np.full(len(sample), -1)
        have = np.zeros(len(sample), bool)
        unmatched = 0
        total = 0
        for a, i in enumerate(ids):
            p = os.path.join(cdir[m], f"{i}.json")
            if not os.path.exists(p):
                continue
            have[a] = True
            preds = parse_response(json.load(open(p)))
            k = 0
            for n_, _c in preds:
                total += 1
                s = matcher.species(n_)
                if s < 0:
                    unmatched += 1
                elif k < 3 and s not in ranked[a]:
                    ranked[a, k] = s
                    k += 1
            if preds:
                g = norm(preds[0][0]).split()[0] if norm(preds[0][0]) else ""
                g = GENUS_ALIAS.get(g, g)
                gl = [norm(x) for x in genus_names]
                gpred[a] = gl.index(g) if g in gl else -1
        if not have.any():
            continue
        r = ranked[have]
        yy = ys[have]
        res = rank_metrics(r, yy, genus_of, genus_pred=gpred[have])
        res["unmatched_answer_share"] = unmatched / max(total, 1)
        result["methods"][f"gemini_{m}"] = dict(overall=res, per_genus=per_genus(r, yy, genus_of, genus_names))
    with open(os.path.join(args.data, f"gemini_results_{args.model}.json"), "w") as f:
        json.dump(result, f, indent=1)
    for k, v in result["methods"].items():
        o = v["overall"]
        print(f"{k}: n={o['n']} top1={o['top1']:.3f} top3={o['top3']:.3f} genus={o['genus_top1']:.3f}")


if __name__ == "__main__":
    main()
