#!/usr/bin/env python3
"""Vollstaendiger Abgleich Bild-Datenbasis <-> Dateibestand der Legacy-Sharing-App.

Eingaben:
  - DB_TSV      : Export aus user_cichlids_pictures (uid, fe_user, deleted, hidden, image)
  - DISK_LIST   : `find <userpics> -type f` -> alle real vorhandenen Dateien

Auswertung:
  1. DB-Referenz vorhanden / fehlt auf Platte
  2. Fuer vorhandene, von der DB referenzierte Bilder: Originalmasse via PIL.
     Laengste Kante <= RESIZE_LONG_EDGE -> VERDAECHTIG (passt zur 450x600-Rendition,
     die restore_images.php nach Crashes in Original-Slots kopierte -> Original evtl. weg).
  3. Verwaiste Dateien: auf Platte, aber von keiner aktiven/geloeschten Bildzeile referenziert.

Ausgabe: Konsolen-Summary (-> 06-image-verification.txt) + CSV-Details.
"""
import csv
import os
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

DB_TSV = "/workspaces/_extract/db_pictures.tsv"
DISK_LIST = "/workspaces/_extract/disk_files.txt"
USERPICS_ROOT = "/workspaces/_extract/data1/userpics"
OUT_DIR = "/workspaces/cichlids.com/workspace-legacy/findings"
RESIZE_LONG_EDGE = 600

os.makedirs(OUT_DIR, exist_ok=True)


def dims(path):
    try:
        with Image.open(path) as im:
            return im.size
    except Exception:
        return None


def main():
    # --- DB-Referenzen laden ---
    rows = []
    with open(DB_TSV, encoding="utf-8", errors="surrogateescape") as fh:
        for line in fh:
            p = line.rstrip("\n").split("\t")
            if len(p) < 5:
                continue
            rows.append((p[0], p[1], p[2], p[3], "\t".join(p[4:])))

    # abs. Pfad -> (uid, deleted) Referenzindex
    ref = {}
    for uid, fe_user, deleted, hidden, image in rows:
        ap = os.path.normpath(os.path.join(USERPICS_ROOT, image.lstrip("/")))
        ref[ap] = (uid, fe_user, deleted, hidden, image)

    # --- Plattenbestand laden ---
    disk = set()
    with open(DISK_LIST, encoding="utf-8", errors="surrogateescape") as fh:
        for line in fh:
            disk.add(os.path.normpath(line.rstrip("\n")))

    ref_paths = set(ref.keys())
    present = ref_paths & disk
    missing = ref_paths - disk
    orphans = disk - ref_paths

    # --- Dimensionen der vorhandenen DB-Bilder ---
    suspect, unreadable = [], []
    ok_original = 0
    hist = {}
    for i, ap in enumerate(present, 1):
        uid, fe_user, deleted, hidden, image = ref[ap]
        d = dims(ap)
        if d is None:
            unreadable.append((uid, fe_user, deleted, hidden, image))
            continue
        w, h = d
        longest = max(w, h)
        b = (longest // 100) * 100
        hist[b] = hist.get(b, 0) + 1
        if longest <= RESIZE_LONG_EDGE:
            suspect.append((uid, fe_user, deleted, hidden, image, w, h, os.path.getsize(ap)))
        else:
            ok_original += 1
        if i % 25000 == 0:
            print(f"  ... {i}/{len(present)} Dimensionen gelesen", flush=True)

    def write_csv(name, header, data):
        with open(os.path.join(OUT_DIR, name), "w", newline="", encoding="utf-8", errors="surrogateescape") as f:
            w = csv.writer(f)
            w.writerow(header)
            w.writerows(data)

    miss_rows = [ref[p] for p in missing]
    write_csv("img_missing.csv", ["uid", "fe_user", "deleted", "hidden", "image"], miss_rows)
    write_csv("img_suspect_resized.csv",
              ["uid", "fe_user", "deleted", "hidden", "image", "width", "height", "bytes"], suspect)
    write_csv("img_unreadable.csv", ["uid", "fe_user", "deleted", "hidden", "image"], unreadable)
    write_csv("img_orphans_sample.csv", ["disk_path"],
              [(p,) for p in sorted(orphans)[:5000]])

    def cnt_active(rs, idx=2):
        return sum(1 for r in rs if r[idx] == "0")

    total_db = len(rows)
    active_db = sum(1 for r in rows if r[2] == "0")

    lines = []
    lines.append("========== BILD-VERIFIKATION: DB <-> PLATTE ==========")
    lines.append(f"DB-Bildverweise gesamt:            {total_db}")
    lines.append(f"  davon aktiv (deleted=0):         {active_db}")
    lines.append(f"Dateien auf Platte (userpics):     {len(disk)}")
    lines.append("")
    lines.append(f"DB-Verweis -> Datei vorhanden:     {len(present)}")
    lines.append(f"  davon Original (> {RESIZE_LONG_EDGE}px Kante):  {ok_original}")
    lines.append(f"  davon VERDAECHTIG klein (<= {RESIZE_LONG_EDGE}px): {len(suspect)}  (aktiv: {cnt_active([ (s[0],s[1],s[2]) for s in suspect ])})")
    lines.append(f"  nicht als Bild lesbar:           {len(unreadable)}")
    lines.append(f"DB-Verweis -> Datei FEHLT:         {len(missing)}  (aktiv: {cnt_active(miss_rows)})")
    lines.append(f"Verwaiste Dateien (Platte, nicht in DB): {len(orphans)}")
    lines.append("")
    lines.append("Histogramm laengste Kante (Bucket px : Anzahl vorhandener DB-Bilder):")
    for b in sorted(hist):
        bar = "#" * min(60, hist[b] * 60 // max(hist.values()))
        lines.append(f"  {b:>5}-{b+99:<5}: {hist[b]:>7}  {bar}")
    lines.append("")
    lines.append("CSV: img_missing.csv / img_suspect_resized.csv / img_unreadable.csv / img_orphans_sample.csv")

    out = "\n".join(lines)
    print(out)
    with open(os.path.join(OUT_DIR, "06-image-verification.txt"), "w", encoding="utf-8") as f:
        f.write(out + "\n")


if __name__ == "__main__":
    main()
