"""Renders data/results.json (and optional Gemini results) as Markdown tables on stdout."""
import json
import sys

d = sys.argv[1] if len(sys.argv) > 1 else "data"
r = json.load(open(f"{d}/results.json"))
pc = lambda x: "-" if x is None else f"{100 * x:.1f}"

print("### Data\n")
for k, v in r["data"].items():
    if k != "species_lt10_test":
        print(f"- {k}: {v}")
print("- species with fewer than 10 test photos:", ", ".join(f"{n} ({c})" for n, c in r["data"]["species_lt10_test"]) or "none")

print("\n### Per model (percent, test split)\n")
print("| Model | Method | Top-1 micro | Top-3 micro | Top-1 macro | Top-3 macro | Genus top-1 (species vote) | Genus top-1 (genus vote) |")
print("|---|---|---|---|---|---|---|---|")
for m, e in r["models"].items():
    rows = [(f"kNN k={k}", v) for k, v in e["knn"].items()]
    for lab, key in (("zero-shot text", "zeroshot"), ("linear probe", "linear_probe"), ("MLP head", "mlp_head")):
        if key in e:
            rows.append((lab, e[key]))
    for lab, v in rows:
        print(f"| {m} | {lab} | {pc(v['top1'])} | {pc(v['top3'])} | {pc(v['macro_top1'])} | {pc(v['macro_top3'])} | "
              f"{pc(v['genus_top1_species'])} | {pc(v['genus_top1_aggregated'])} |")

print(f"\nBest kNN configuration: {r['best']}; best linear probe model: {r.get('best_probe_model')}\n")
print("### Groups of 3 photos of one member and species\n")
print("| Scorer | Groups | Photos | Species | Single top-1 | Single top-3 | Group top-1 | Group top-3 | Group genus top-1 |")
print("|---|---|---|---|---|---|---|---|---|")
for lab, g in r["groups"].items():
    print(f"| {g['model']} k={g['k']} ({lab}) | {g['groups']} | {g['photos']} | {g['species']} | {pc(g['single']['top1'])} | "
          f"{pc(g['single']['top3'])} | {pc(g['grouped']['top1'])} | {pc(g['grouped']['top3'])} | {pc(g['grouped']['genus_top1_aggregated'])} |")

print("\n### Genus fallback (best kNN configuration)\n")
print("| Margin threshold | Species answers (coverage) | Species top-1 when answered | Species top-3 when answered | Genus fallbacks | Genus accuracy of fallbacks | Overall correct (species or genus) |")
print("|---|---|---|---|---|---|---|")
for c in r["fallback"]:
    print(f"| {c['threshold']} | {pc(c['coverage'])} | {pc(c['species_top1_answered'])} | {pc(c['species_top3_answered'])} | "
          f"{pc(c['genus_fallback_share'])} | {pc(c['genus_acc_fallback'])} | {pc(c['overall_correct'])} |")

print("\n### Intra-genus accuracy\n")
ig = r["intra_genus"]
print(f"kNN: {ig['knn_model']} k={ig['knn_k']}; linear probe: {ig['probe_model']}\n")
print("| Genus | Species | Test photos | Chance top-1 | kNN top-1 | kNN top-3 | Probe top-1 | Probe top-3 |")
print("|---|---|---|---|---|---|---|---|")
for x in ig["rows"]:
    print(f"| {x['genus']} | {x['species']} | {x['test_photos']} | {pc(x['chance_top1'])} | {pc(x['knn_top1'])} | {pc(x['knn_top3'])} | "
          f"{pc(x.get('probe_top1'))} | {pc(x.get('probe_top3'))} |")

print("\n### Most confused species pairs (best kNN, top-1 errors, both directions summed)\n")
print("| Species A | Species B | Errors | Same genus |")
print("|---|---|---|---|")
for x in r["confused_pairs"]:
    print(f"| {x['a']} | {x['b']} | {x['errors']} | {'yes' if x['same_genus'] else 'no'} |")
print("\n### Ten worst species by top-3 (at least 10 test photos)\n")
print("| Species | Test photos | Top-1 | Top-3 |")
print("|---|---|---|---|")
for x in r["worst_species"]:
    print(f"| {x['species']} | {x['test_photos']} | {pc(x['top1'])} | {pc(x['top3'])} |")
print("\n### Five best species by top-3\n")
for x in r["best_species"]:
    print(f"- {x['species']}: {x['test_photos']} test photos, top-3 {pc(x['top3'])}")
