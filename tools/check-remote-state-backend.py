#!/usr/bin/env python3
"""Asserts every OpenTofu tier uses a remote state backend, never implicit local state.

The stateful tier's OpenTofu state is the only management handle to the protected resources;
losing a local state file would orphan them and undermine the destructive-change safeguards.
This check enforces ADR-0016: each tier that has OpenTofu code must declare a remote
`backend "s3"`, and the dedicated state bucket must not be one of the buckets the foundation
manages (media-master/backup) — otherwise the state would live inside a resource its own state
creates.

Parses the .tf sources directly (no cloud credentials, runs offline in CI). `--self-test`
exercises the parser against fixtures so the guard cannot silently rot.
Story: task-2.13
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
INFRA = os.path.join(os.path.dirname(HERE), "infra")

TIERS = ("foundation", "app")
# Variable names in the foundation whose defaults are the buckets that tier manages; the state
# bucket must differ from each of them.
MANAGED_BUCKET_VARS = ("media_master_bucket_name", "backup_bucket_name")


def read_tf(tier_dir):
    text = []
    for path in sorted(glob.glob(os.path.join(tier_dir, "*.tf"))):
        with open(path, encoding="utf-8") as fh:
            text.append(fh.read())
    return "\n".join(text)


def backend_bucket(tf_text):
    """Return the bucket of a `backend "s3"` block, "" if the block has no bucket, or None
    if there is no s3 backend block at all."""
    m = re.search(r'backend\s+"s3"\s*\{', tf_text)
    if not m:
        return None
    depth, i = 1, m.end()
    while i < len(tf_text) and depth > 0:
        if tf_text[i] == "{":
            depth += 1
        elif tf_text[i] == "}":
            depth -= 1
        i += 1
    body = tf_text[m.end():i - 1]
    b = re.search(r'bucket\s*=\s*"([^"]+)"', body)
    return b.group(1) if b else ""


def managed_bucket_names(tf_text):
    names = set()
    for var in MANAGED_BUCKET_VARS:
        m = re.search(
            r'variable\s+"' + re.escape(var) + r'"\s*\{.*?default\s*=\s*"([^"]+)"',
            tf_text, re.DOTALL,
        )
        if m:
            names.add(m.group(1))
    return names


def check_tier(tier, tf_text, managed):
    failures = []
    bucket = backend_bucket(tf_text)
    if bucket is None:
        failures.append(f'{tier}: no remote backend "s3" block (implicit local state is unsafe, ADR-0016)')
        return failures
    if not bucket:
        failures.append(f"{tier}: backend has no bucket set")
        return failures
    if bucket in managed:
        failures.append(f"{tier}: state bucket '{bucket}' must not be a managed media-master/backup bucket")
    return failures


def main(argv):
    if "--self-test" in argv:
        return self_test()
    failures = []
    present = 0
    managed = managed_bucket_names(read_tf(os.path.join(INFRA, "foundation")))
    for tier in TIERS:
        tier_dir = os.path.join(INFRA, tier)
        if not glob.glob(os.path.join(tier_dir, "*.tf")):
            continue  # tier not built yet
        present += 1
        failures += check_tier(tier, read_tf(tier_dir), managed)
    if failures:
        print("remote state backend check failed (ADR-0016):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print(f"remote state backend OK ({present} tier(s) checked)")
    return 0


def self_test():
    good = 'terraform {\n  backend "s3" {\n    bucket = "cichlids-tfstate"\n  }\n}'
    no_backend = 'terraform {\n  required_version = ">= 1.10.0"\n}'
    collision = 'terraform {\n  backend "s3" {\n    bucket = "cichlids-media-master"\n  }\n}'
    no_bucket = 'terraform {\n  backend "s3" {\n    key = "x"\n  }\n}'
    managed = {"cichlids-media-master", "cichlids-backup"}
    cases = [
        ("good", good, 0),
        ("missing backend", no_backend, 1),
        ("bucket collides with managed", collision, 1),
        ("backend without bucket", no_bucket, 1),
    ]
    errors = []
    for label, text, want in cases:
        got = len(check_tier("t", text, managed))
        if got != want:
            errors.append(f"{label}: expected {want} failure(s), got {got}")
    # The managed-name parser must read the foundation variable defaults.
    sample_vars = (
        'variable "media_master_bucket_name" {\n  default = "cichlids-media-master"\n}\n'
        'variable "backup_bucket_name" {\n  default = "cichlids-backup"\n}\n'
    )
    if managed_bucket_names(sample_vars) != managed:
        errors.append("managed_bucket_names did not parse the variable defaults")
    if errors:
        print("remote-state-backend self-test FAILED:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    print("remote-state-backend self-test OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
