"""Builds data/pictures.csv from the read-only legacy database (SELECT only).

Each row is one picture with exactly one species tag. No member names or other
personal data are written, only numeric member ids.
"""
import argparse
import csv
import os
import re
import subprocess
from collections import defaultdict

ROOT = "/workspaces/legacy-data/images/userpics"


def query(sql):
    out = subprocess.run(
        ["docker", "exec", "cichlids-legacy-db-1", "mysql", "-uroot", "-plegacy",
         "cichlids_typo3", "--batch", "--raw", "--default-character-set=binary", "--skip-column-names", "-e", sql],
        capture_output=True, check=True).stdout.decode("utf-8", errors="surrogateescape")
    return [line.split("\t") for line in out.splitlines()]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="data")
    ap.add_argument("--min-candidates", type=int, default=90,
                    help="species kept for embedding (buffer below the final 100 threshold)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    species = {}
    for uid, title, genus_id, gname in query(
        "select s.uid, s.title, s.genus, coalesce(g.title,'') from user_cichlids_species s "
        "left join user_cichlids_genus_names g on g.uid=s.genus where s.deleted=0"):
        species[uid] = (title.strip(), gname.strip())

    tags = defaultdict(set)
    for pic, sp in query("select distinct uid_local, uid_foreign from user_cichlids_species_pictures_mm where uid_foreign>0"):
        tags[pic].add(sp)

    pics = {}
    for uid, fe_user, image in query(
        "select uid, fe_user, image from user_cichlids_pictures where deleted=0 and hidden=0 and image<>''"):
        pics[uid] = (fe_user, image)

    tank_of = {}
    for tuid, tank_images, image in query("select uid, tank_images, image from user_cichlids_tanks"):
        ids = [x for x in re.split(r"[,\s]+", tank_images) if x.isdigit()]
        if image.isdigit():
            ids.append(image)
        for i in ids:
            tank_of.setdefault(i, tuid)

    stats = dict(tagged_pictures=0, multi_tag=0, single_tag=0, single_tag_visible=0,
                 single_tag_unknown_species=0, single_tag_missing_file=0)
    rows = []
    for pic, sps in tags.items():
        stats["tagged_pictures"] += 1
        if len(sps) > 1:
            stats["multi_tag"] += 1
            continue
        stats["single_tag"] += 1
        if pic not in pics:
            continue
        sp = next(iter(sps))
        if sp not in species:
            stats["single_tag_unknown_species"] += 1
            continue
        stats["single_tag_visible"] += 1
        fe_user, image = pics[pic]
        rel = image[len("user_pics/"):] if image.startswith("user_pics/") else image
        path = os.path.join(ROOT, "user_pics", rel)
        if not os.path.isfile(path):
            stats["single_tag_missing_file"] += 1
            continue
        member = fe_user if fe_user != "0" else None
        if member is None:
            m = re.match(r"user_pics/(\d+)/", image)
            member = m.group(1) if m else "anon"
        rows.append(dict(pic=pic, path=path, member=member, tank=tank_of.get(pic, ""),
                         species_id=sp, species=species[sp][0], genus=species[sp][1]))

    count = defaultdict(int)
    for r in rows:
        count[r["species_id"]] += 1
    rows = [r for r in rows if count[r["species_id"]] >= args.min_candidates]
    with open(os.path.join(args.out, "pictures.csv"), "w", newline="", encoding="utf-8", errors="surrogateescape") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    stats["candidate_species"] = len({r["species_id"] for r in rows})
    stats["candidate_pictures"] = len(rows)
    stats["species_ge_100_files_present"] = sum(1 for v in count.values() if v >= 100)
    for k, v in stats.items():
        print(f"{k}: {v}")


if __name__ == "__main__":
    main()
