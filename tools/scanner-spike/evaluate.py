"""Nearest-neighbour species recognition metrics on cached embeddings.

Reads data/pictures.csv and cache/<model>/, builds a member-disjoint split and
prints/writes all tables (data/tables.md, data/results.json).
"""
import argparse
import csv
import json
import os
import random
from collections import Counter, defaultdict

import numpy as np

MIN_PHOTOS = 100
TEST_FRACTION = 0.2
TAU = 0.05
KS = (5, 10, 20)


def load_rows(path):
    with open(path, newline="", encoding="utf-8", errors="surrogateescape") as f:
        return list(csv.DictReader(f))


def make_split(rows, valid, seed):
    """Per species: whole members go to the test side until about 20% of photos are reached."""
    by_species = defaultdict(list)
    for i, r in enumerate(rows):
        if valid[i]:
            by_species[r["species_id"]].append(i)
    selected = {s: idx for s, idx in by_species.items() if len(idx) >= MIN_PHOTOS}
    rng = random.Random(seed)
    is_test = np.zeros(len(rows), dtype=bool)
    for s, idx in sorted(selected.items()):
        groups = defaultdict(list)
        for i in idx:
            groups[rows[i]["member"]].append(i)
        target = round(TEST_FRACTION * len(idx))
        keys = sorted(g for g in groups if g != "anon")
        rng.shuffle(keys)
        got = 0
        for g in keys:
            if got >= target:
                break
            if got + len(groups[g]) > 1.5 * target + 2:
                continue
            for i in groups[g]:
                is_test[i] = True
            got += len(groups[g])
    return selected, is_test


def normalize(x):
    x = x.astype(np.float32)
    return x / np.maximum(np.linalg.norm(x, axis=1, keepdims=True), 1e-8)


def species_scores(sims_top, labels_top, n_species, k):
    """Softmax-weighted vote of the top-k neighbours, normalized to sum 1."""
    s = sims_top[:, :k]
    w = np.exp((s - s[:, :1]) / TAU)
    out = np.zeros((len(s), n_species), dtype=np.float32)
    for j in range(k):
        np.add.at(out, (np.arange(len(s)), labels_top[:, j]), w[:, j])
    return out / out.sum(1, keepdims=True)


def knn_scores(test_x, ref_x, ref_y, n_species, k):
    top_s, top_l = [], []
    for a in range(0, len(test_x), 2000):
        sim = test_x[a:a + 2000] @ ref_x.T
        idx = np.argpartition(-sim, max(KS), axis=1)[:, :max(KS)]
        ss = np.take_along_axis(sim, idx, 1)
        order = np.argsort(-ss, 1)
        idx = np.take_along_axis(idx, order, 1)
        top_s.append(np.take_along_axis(sim, idx, 1))
        top_l.append(ref_y[idx])
    return species_scores(np.concatenate(top_s), np.concatenate(top_l), n_species, k)


def metrics(scores, y, genus_of, n_species):
    top3 = np.argsort(-scores, 1)[:, :3]
    hit1 = top3[:, 0] == y
    hit3 = (top3 == y[:, None]).any(1)
    macro1 = np.mean([hit1[y == c].mean() for c in np.unique(y)])
    macro3 = np.mean([hit3[y == c].mean() for c in np.unique(y)])
    g_true = genus_of[y]
    g_top1sp = genus_of[top3[:, 0]]
    n_gen = genus_of.max() + 1
    gscore = np.zeros((len(scores), n_gen), dtype=np.float32)
    for c in range(n_species):
        gscore[:, genus_of[c]] += scores[:, c]
    g_agg = gscore.argmax(1)
    return dict(top1=float(hit1.mean()), top3=float(hit3.mean()), macro_top1=float(macro1), macro_top3=float(macro3),
                genus_top1_species=float((g_top1sp == g_true).mean()), genus_top1_aggregated=float((g_agg == g_true).mean()))


def train_head(Xtr, ytr, n_classes, hidden=0, epochs=60, seed=0):
    """Linear probe (hidden=0) or one-hidden-layer MLP on frozen normalized embeddings, CPU only."""
    import torch
    torch.manual_seed(seed)
    d = Xtr.shape[1]
    net = torch.nn.Linear(d, n_classes) if not hidden else torch.nn.Sequential(
        torch.nn.Linear(d, hidden), torch.nn.GELU(), torch.nn.Dropout(0.2), torch.nn.Linear(hidden, n_classes))
    opt = torch.optim.AdamW(net.parameters(), lr=2e-3, weight_decay=1e-2)
    X = torch.from_numpy(Xtr * 10.0)
    y = torch.from_numpy(ytr.astype(np.int64))
    sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=epochs)
    for _ in range(epochs):
        net.train()
        perm = torch.randperm(len(X))
        for a in range(0, len(X), 256):
            b = perm[a:a + 256]
            opt.zero_grad()
            torch.nn.functional.cross_entropy(net(X[b]), y[b], label_smoothing=0.05).backward()
            opt.step()
        sched.step()
    net.eval()
    return lambda Xte: torch.softmax(net(torch.from_numpy(Xte * 10.0)), 1).detach().numpy()


def intra_genus(rows_genus, genus_of, genus_names, y_test, y_ref, scores_fn_knn, probe_p, test_idx, ref_idx, X, k, n_sp,
                wanted=("Tropheus", "Maylandia", "Metriaclima", "Aulonocara", "Pseudotropheus", "Labidochromis", "Neolamprologus")):
    out = []
    for gname in wanted:
        if gname not in genus_names:
            continue
        g = genus_names.index(gname)
        sps = [c for c in range(n_sp) if genus_of[c] == g]
        if len(sps) < 2:
            continue
        remap = {c: i for i, c in enumerate(sps)}
        tm = np.isin(y_test, sps)
        rm = np.isin(y_ref, sps)
        if tm.sum() == 0:
            continue
        yt = np.array([remap[c] for c in y_test[tm]])
        yr = np.array([remap[c] for c in y_ref[rm]])
        row = dict(genus=gname, species=len(sps), test_photos=int(tm.sum()), reference_photos=int(rm.sum()),
                   chance_top1=1 / len(sps))
        kk = min(k, int(rm.sum()) - 1)
        sc = knn_scores(X[test_idx[tm]], X[ref_idx[rm]], yr, len(sps), kk)
        order = np.argsort(-sc, 1)
        row["knn_top1"] = float((order[:, 0] == yt).mean())
        row["knn_top3"] = float((order[:, :3] == yt[:, None]).any(1).mean())
        if probe_p is not None:
            p = probe_p[tm][:, sps]
            order = np.argsort(-p, 1)
            row["probe_top1"] = float((order[:, 0] == yt).mean())
            row["probe_top3"] = float((order[:, :3] == yt[:, None]).any(1).mean())
        out.append(row)
    return out


def text_embeddings(model_key, names):
    import torch
    import open_clip
    from embed import MODELS
    cfg = MODELS[model_key]
    kw = dict(pretrained=cfg["pretrained"]) if "pretrained" in cfg else {}
    model, _, _ = open_clip.create_model_and_transforms(cfg["name"], **kw)
    tok = open_clip.get_tokenizer(cfg["name"])
    model.eval()
    with torch.inference_mode():
        t = model.encode_text(tok([f"a photo of {n}." for n in names])).float().numpy()
    return normalize(t)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--models", default="bioclip,bioclip2,dinov2,clip")
    ap.add_argument("--csv", default="data/pictures.csv")
    ap.add_argument("--cache", default="cache")
    ap.add_argument("--out", default="data")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--group", type=int, default=3)
    args = ap.parse_args()
    models = args.models.split(",")
    rows = load_rows(args.csv)
    states = {m: np.load(f"{args.cache}/{m}/state.npy") for m in models}
    valid = np.ones(len(rows), dtype=bool)
    for st in states.values():
        valid &= st == 1
    undecodable = int((states[models[0]] == 2).sum())
    selected, is_test = make_split(rows, valid, args.seed)
    sp_ids = sorted(selected)
    sp_index = {s: i for i, s in enumerate(sp_ids)}
    sp_name = [next(rows[i]["species"] for i in selected[s][:1]) for s in sp_ids]
    genus_names = sorted({rows[selected[s][0]]["genus"] for s in sp_ids})
    gidx = {g: i for i, g in enumerate(genus_names)}
    genus_of = np.array([gidx[rows[selected[s][0]]["genus"]] for s in sp_ids])
    sel_idx = np.array(sorted(i for s in sp_ids for i in selected[s]))
    y_all = np.full(len(rows), -1)
    for s in sp_ids:
        for i in selected[s]:
            y_all[i] = sp_index[s]
    ref_idx = sel_idx[~is_test[sel_idx]]
    test_idx = sel_idx[is_test[sel_idx]]
    y_ref, y_test = y_all[ref_idx], y_all[test_idx]
    n_sp = len(sp_ids)
    test_counts = Counter(y_test.tolist())
    few_test = sorted((sp_name[c], test_counts.get(c, 0)) for c in range(n_sp) if test_counts.get(c, 0) < 10)
    keep_sp = np.array([test_counts.get(c, 0) > 0 for c in range(n_sp)])

    res = dict(args=vars(args), data=dict(
        candidate_rows=len(rows), undecodable=undecodable, valid=int(valid.sum()),
        species_ge_100=n_sp, genera=len(genus_names), selected_photos=len(sel_idx),
        reference=len(ref_idx), test=len(test_idx), members_selected=len({rows[i]["member"] for i in sel_idx}),
        species_without_test=int((~keep_sp).sum()), species_lt10_test=few_test,
        tank_known_selected=int(sum(1 for i in sel_idx if rows[i]["tank"]))))
    print(json.dumps({k: v for k, v in res["data"].items() if k != "species_lt10_test"}), flush=True)
    print("species with fewer than 10 test photos:", len(few_test), flush=True)

    feats = {}
    for m in models:
        feats[m] = normalize(np.load(f"{args.cache}/{m}/emb.npy"))
    res["models"] = {}
    all_scores = {}
    probe_store = {}
    for m in models:
        X = feats[m]
        entry = {"knn": {}}
        for k in KS:
            sc = knn_scores(X[test_idx], X[ref_idx], y_ref, n_sp, k)
            all_scores[(m, k)] = sc
            entry["knn"][k] = metrics(sc, y_test, genus_of, n_sp)
            print(m, "k", k, entry["knn"][k], flush=True)
        if m in ("bioclip", "bioclip2", "clip"):
            names = sp_name
            T = text_embeddings(m, names)
            logits = 100 * X[test_idx] @ T.T
            logits -= logits.max(1, keepdims=True)
            p = np.exp(logits)
            p /= p.sum(1, keepdims=True)
            entry["zeroshot"] = metrics(p, y_test, genus_of, n_sp)
            print(m, "zero-shot", entry["zeroshot"], flush=True)
        probes = {}
        for label, hidden in (("linear_probe", 0), ("mlp_head", 512)):
            pr = train_head(X[ref_idx], y_ref, n_sp, hidden)(X[test_idx])
            probes[label] = pr
            entry[label] = metrics(pr, y_test, genus_of, n_sp)
            print(m, label, entry[label], flush=True)
        probe_store[m] = probes
        res["models"][m] = entry

    # ensemble of all models (concatenated normalized embeddings)
    if len(models) > 1:
        Xe = normalize(np.concatenate([feats[m] for m in models], 1))
        entry = {"knn": {}}
        for k in KS:
            sc = knn_scores(Xe[test_idx], Xe[ref_idx], y_ref, n_sp, k)
            all_scores[("ensemble", k)] = sc
            entry["knn"][k] = metrics(sc, y_test, genus_of, n_sp)
        res["models"]["ensemble"] = entry

    # best configuration by macro top-3
    best = max(((m, k) for (m, k) in all_scores if m != "ensemble"), key=lambda mk: res["models"][mk[0]]["knn"][mk[1]]["macro_top3"])
    res["best"] = dict(model=best[0], k=best[1])
    if "ensemble" in res["models"]:
        eb = max(KS, key=lambda k: res["models"]["ensemble"]["knn"][k]["macro_top3"])
        res["best_ensemble_k"] = eb
    sc = all_scores[best]
    print("best", best, flush=True)

    # groups of consecutive photos of one member (same tank where known) and one species
    grp = defaultdict(list)
    for j, i in enumerate(test_idx):
        grp[(y_test[j], rows[i]["member"])].append(j)
    triples = []
    for (c, mem), js in grp.items():
        js.sort(key=lambda j: (rows[test_idx[j]]["tank"], int(rows[test_idx[j]]["pic"])))
        for a in range(0, len(js) - args.group + 1, args.group):
            triples.append(js[a:a + args.group])
    res["groups"] = {}
    for label, key in (("best_single_model", best), ("ensemble", ("ensemble", res.get("best_ensemble_k", 10)))):
        if key not in all_scores:
            continue
        s = all_scores[key]
        flat = [j for t in triples for j in t]
        yy_single = y_test[flat]
        single = metrics(s[flat], yy_single, genus_of, n_sp)
        gs = np.stack([s[t].mean(0) for t in triples])
        gy = np.array([y_test[t[0]] for t in triples])
        grouped = metrics(gs, gy, genus_of, n_sp)
        res["groups"][label] = dict(model=key[0], k=key[1], groups=len(triples), photos=len(flat),
                                    species=len(set(gy.tolist())), single=single, grouped=grouped)
        print("groups", label, res["groups"][label], flush=True)

    # fallback curve
    top = np.argsort(-sc, 1)
    top1 = top[:, 0]
    margin = sc[np.arange(len(sc)), top1] - sc[np.arange(len(sc)), top[:, 1]]
    gscore = np.zeros((len(sc), len(genus_names)), dtype=np.float32)
    for c in range(n_sp):
        gscore[:, genus_of[c]] += sc[:, c]
    g_pred = gscore.argmax(1)
    g_true = genus_of[y_test]
    hit3 = (top[:, :3] == y_test[:, None]).any(1)
    curve = []
    for th in (0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8):
        ans = margin >= th
        fb = ~ans
        curve.append(dict(threshold=th, coverage=float(ans.mean()),
                          species_top1_answered=float((top1[ans] == y_test[ans]).mean()) if ans.any() else None,
                          species_top3_answered=float(hit3[ans].mean()) if ans.any() else None,
                          genus_fallback_share=float(fb.mean()),
                          genus_acc_fallback=float((g_pred[fb] == g_true[fb]).mean()) if fb.any() else None,
                          overall_correct=float(np.where(ans, top1 == y_test, g_pred == g_true).mean())))
    res["fallback"] = curve
    for c in curve:
        print("fallback", c, flush=True)

    # intra-genus difficulty for the best kNN and the best linear probe
    best_probe_model = max(models, key=lambda m: res["models"][m]["linear_probe"]["macro_top3"])
    res["best_probe_model"] = best_probe_model
    gen_rows = intra_genus(None, genus_of, genus_names, y_test, y_ref, None, probe_store[best_probe_model]["linear_probe"],
                           test_idx, ref_idx, feats[best[0]], best[1], n_sp)
    # the probe columns come from the best probe model, kNN columns from the best kNN model
    res["intra_genus"] = dict(knn_model=best[0], knn_k=best[1], probe_model=best_probe_model, rows=gen_rows)
    for r in gen_rows:
        print("intra-genus", r, flush=True)

    # hard cases
    conf = Counter()
    for t, p in zip(y_test, top1):
        if t != p:
            conf[tuple(sorted((t, p)))] += 1
    res["confused_pairs"] = [dict(a=sp_name[a], b=sp_name[b], errors=n, same_genus=bool(genus_of[a] == genus_of[b]))
                             for (a, b), n in conf.most_common(10)]
    directed = Counter((t, p) for t, p in zip(y_test, top1) if t != p)
    res["confused_directed"] = [dict(true=sp_name[t], predicted=sp_name[p], errors=n) for (t, p), n in directed.most_common(10)]
    per = []
    for c in range(n_sp):
        m_ = y_test == c
        if m_.sum() >= 10:
            per.append((float(hit3[m_].mean()), sp_name[c], int(m_.sum()), float((top1[m_] == c).mean())))
    per.sort()
    res["worst_species"] = [dict(species=n, top3=a, top1=t1, test_photos=nn) for a, n, nn, t1 in per[:10]]
    res["best_species"] = [dict(species=n, top3=a, top1=t1, test_photos=nn) for a, n, nn, t1 in per[-5:]]

    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "results.json"), "w") as f:
        json.dump(res, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    arrays = {}
    for m in sorted({m for m, _ in all_scores}):
        kb = max(KS, key=lambda kk: res["models"][m]["knn"][kk]["macro_top3"])
        arrays[f"knn_{m}_k{kb}"] = all_scores[(m, kb)].astype(np.float16)
    for m, probes in probe_store.items():
        for label, pr in probes.items():
            arrays[f"{label}_{m}"] = pr.astype(np.float16)
    np.savez_compressed(os.path.join(args.out, "scores.npz"), **arrays)
    np.savez_compressed(os.path.join(args.out, "split.npz"), test_idx=test_idx, ref_idx=ref_idx, y_test=y_test,
                        y_ref=y_ref, genus_of=genus_of)
    with open(os.path.join(args.out, "split_names.json"), "w") as f:
        json.dump(dict(species=sp_name, genera=genus_names, species_ids=sp_ids), f)
    print("done", flush=True)


if __name__ == "__main__":
    main()
