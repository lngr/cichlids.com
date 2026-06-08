#!/usr/bin/env python3
"""Asserts the three-project capability boundary (ADR-0017) holds in the OpenTofu sources.

The routine automation token exists only in the primary project (`cichlids`), which holds all
live, rebuildable infrastructure. The irreplaceable backups live in a separate backup project
that no cloud token ever reaches; isolation is by project, because the provider's tokens cannot
be scoped below Read & Write. This check enforces that boundary statically:

  - the backup tier declares no Hetzner Cloud provider and no cloud token (only an
    object-storage credential reaches it);
  - the backup tier holds the immutable backup buckets, each undeletable by construction
    (prevent_destroy) and under Object Lock Compliance;
  - the primary foundation tier keeps the single cloud-token tier and no longer holds a backup
    bucket, while the media originals stay there.

Parses the .tf sources directly (no cloud credentials, runs offline in CI). `--self-test`
exercises the checks against fixtures so the guard cannot silently rot.
Story: task-2.16
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.join(os.path.dirname(HERE), "infra")

# Immutable backup buckets the backup project must hold (ADR-0013, ADR-0017): the database
# PITR/WAL backups and the off-master copy of the media originals.
BACKUP_BUCKETS = ("database_backup", "media_master_backup")


def read_tf(tier_dir):
    parts = []
    for path in sorted(glob.glob(os.path.join(tier_dir, "*.tf"))):
        with open(path, encoding="utf-8") as fh:
            parts.append(fh.read())
    return "\n".join(parts)


def resource_blocks(tf_text):
    """Return {(type, name): body} for every resource block in tf_text (brace-matched)."""
    blocks = {}
    for m in re.finditer(r'resource\s+"([^"]+)"\s+"([^"]+)"\s*\{', tf_text):
        depth, i = 1, m.end()
        while i < len(tf_text) and depth > 0:
            if tf_text[i] == "{":
                depth += 1
            elif tf_text[i] == "}":
                depth -= 1
            i += 1
        blocks[(m.group(1), m.group(2))] = tf_text[m.end():i - 1]
    return blocks


def has_provider(tf_text, name):
    return re.search(r'provider\s+"' + re.escape(name) + r'"\s*\{', tf_text) is not None


def has_variable(tf_text, name):
    return re.search(r'variable\s+"' + re.escape(name) + r'"\s*\{', tf_text) is not None


def check_backup_tier(tf_text):
    failures = []
    if not tf_text.strip():
        return ["backup: infra/backup/ has no OpenTofu sources (the backup project tier is missing)"]
    if has_provider(tf_text, "hcloud"):
        failures.append('backup: declares a "hcloud" provider — the backup project must hold no cloud token (ADR-0017)')
    if has_variable(tf_text, "hcloud_token"):
        failures.append("backup: declares a hcloud_token variable — no cloud token may exist in the backup tier (ADR-0017)")
    blocks = resource_blocks(tf_text)
    for name in BACKUP_BUCKETS:
        body = blocks.get(("aws_s3_bucket", name))
        if body is None:
            failures.append(f"backup: missing immutable backup bucket aws_s3_bucket.{name}")
            continue
        if not re.search(r"prevent_destroy\s*=\s*true", body):
            failures.append(f"backup: aws_s3_bucket.{name} missing lifecycle prevent_destroy = true")
    if "COMPLIANCE" not in tf_text:
        failures.append("backup: no Object Lock COMPLIANCE configuration for the backup buckets (ADR-0013/0017)")
    return failures


def check_foundation_tier(tf_text):
    failures = []
    blocks = resource_blocks(tf_text)
    if not has_provider(tf_text, "hcloud"):
        failures.append('foundation: missing "hcloud" provider — the primary project holds the single cloud-token tier (ADR-0017)')
    for name in BACKUP_BUCKETS + ("backup",):
        if ("aws_s3_bucket", name) in blocks:
            failures.append(f"foundation: still defines aws_s3_bucket.{name} — backup buckets belong in the backup project (ADR-0017)")
    if ("aws_s3_bucket", "media_master") not in blocks:
        failures.append("foundation: missing aws_s3_bucket.media_master — the media originals stay in the primary project (ADR-0017)")
    return failures


def main(argv):
    if "--self-test" in argv:
        return self_test()
    failures = []
    failures += check_foundation_tier(read_tf(os.path.join(INFRA, "foundation")))
    failures += check_backup_tier(read_tf(os.path.join(INFRA, "backup")))
    if failures:
        print("project separation check failed (ADR-0017):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print("project separation OK (primary holds the only cloud token; backups isolated in their own project)")
    return 0


def self_test():
    good_foundation = (
        'provider "hcloud" {}\n'
        'resource "hcloud_server" "node" {}\n'
        'resource "aws_s3_bucket" "media_master" {}\n'
    )
    good_backup = (
        'provider "aws" {}\n'
        'variable "s3_access_key" {}\n'
        'resource "aws_s3_bucket" "database_backup" {\n  lifecycle { prevent_destroy = true }\n}\n'
        'resource "aws_s3_bucket" "media_master_backup" {\n  lifecycle { prevent_destroy = true }\n}\n'
        'resource "aws_s3_bucket_object_lock_configuration" "database_backup" {\n'
        '  rule { default_retention { mode = "COMPLIANCE" } }\n}\n'
    )
    foundation_cases = [
        ("good foundation", good_foundation, 0),
        ("foundation without cloud provider", good_foundation.replace('provider "hcloud" {}\n', ""), 1),
        ("foundation still holds backup bucket",
         good_foundation + 'resource "aws_s3_bucket" "database_backup" {}\n', 1),
        ("foundation lost media_master",
         good_foundation.replace('resource "aws_s3_bucket" "media_master" {}\n', ""), 1),
    ]
    backup_cases = [
        ("good backup", good_backup, 0),
        ("backup with cloud provider", 'provider "hcloud" {}\n' + good_backup, 1),
        ("backup with cloud token var", good_backup + 'variable "hcloud_token" {}\n', 1),
        ("backup missing a bucket",
         good_backup.replace('resource "aws_s3_bucket" "media_master_backup" {\n  lifecycle { prevent_destroy = true }\n}\n', ""), 1),
        ("backup bucket without prevent_destroy",
         good_backup.replace("  lifecycle { prevent_destroy = true }\n", "", 1), 1),
        ("backup without compliance lock", good_backup.replace('"COMPLIANCE"', '"GOVERNANCE"'), 1),
        ("backup tier missing entirely", "", 1),
    ]
    errors = []
    for label, text, want in foundation_cases:
        got = len(check_foundation_tier(text))
        if (got > 0) != (want > 0):
            errors.append(f"foundation/{label}: expected {'failures' if want else 'pass'}, got {got} failure(s)")
    for label, text, want in backup_cases:
        got = len(check_backup_tier(text))
        if (got > 0) != (want > 0):
            errors.append(f"backup/{label}: expected {'failures' if want else 'pass'}, got {got} failure(s)")
    if errors:
        print("project-separation self-test FAILED:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("project-separation self-test OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
