#!/usr/bin/env python3
"""Asserts the Layer 1 safeguards (ADR-0014) are present on every critical platform resource:
lifecycle prevent_destroy on all of them, Hetzner delete_protection on the node and database
volume, and versioning on the media-master bucket. Run before adding the safeguards it fails
(red); with them in place it passes (green).

The immutable backup buckets live in the separate backup project (ADR-0017) and are checked by
infra/backup/guardrails_test.py.

Parses the .tf sources directly (brace-matched resource blocks) so it needs no cloud
credentials and runs offline in CI.
Story: task-2.2, task-2.18, task-2.22
"""
import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# (resource type, resource name, needs delete_protection)
CRITICAL = [
    ("hcloud_server", "node", True),
    ("hcloud_volume", "database", True),
    ("aws_s3_bucket", "media_master", False),
    ("hcloud_zone", "primary", True),
]


def load_blocks():
    """Return {(type, name): block_body} for every resource block in the directory."""
    blocks = {}
    for path in glob.glob(os.path.join(HERE, "*.tf")):
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        for m in re.finditer(r'resource\s+"([^"]+)"\s+"([^"]+)"\s*\{', text):
            rtype, rname = m.group(1), m.group(2)
            depth, i = 1, m.end()
            while i < len(text) and depth > 0:
                if text[i] == "{":
                    depth += 1
                elif text[i] == "}":
                    depth -= 1
                i += 1
            blocks[(rtype, rname)] = text[m.end():i - 1]
    return blocks


def main():
    blocks = load_blocks()
    failures = []

    for rtype, rname, needs_dp in CRITICAL:
        body = blocks.get((rtype, rname))
        if body is None:
            failures.append(f"{rtype}.{rname}: critical resource is missing")
            continue
        if not re.search(r"prevent_destroy\s*=\s*true", body):
            failures.append(f"{rtype}.{rname}: missing lifecycle prevent_destroy = true")
        if needs_dp and not re.search(r"delete_protection\s*=\s*true", body):
            failures.append(f"{rtype}.{rname}: missing delete_protection = true")

    for bucket in ("media_master",):
        vbody = blocks.get(("aws_s3_bucket_versioning", bucket))
        if vbody is None or "Enabled" not in vbody:
            failures.append(f"aws_s3_bucket_versioning.{bucket}: versioning is not Enabled")

    if failures:
        print("platform guardrails missing (ADR-0014):", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print(f"platform guardrails OK ({len(CRITICAL)} critical resources protected)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
